using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

internal static class CecilTypeNames
{
    public static string ToMetadataName(INamedTypeSymbol typeSymbol)
    {
        if (typeSymbol.ContainingType != null)
        {
            return ToMetadataName(typeSymbol.ContainingType) + "/" + typeSymbol.MetadataName;
        }

        if (typeSymbol.ContainingNamespace == null || typeSymbol.ContainingNamespace.IsGlobalNamespace)
        {
            return typeSymbol.MetadataName;
        }

        return ToNamespaceMetadataName(typeSymbol.ContainingNamespace) + "." + typeSymbol.MetadataName;
    }

    /// <summary>
    /// Builds the dotted namespace name as metadata records it, for both metadata and reflection
    /// type names. Returns an empty string for the global namespace.
    /// </summary>
    // Why the names and not ToDisplayString: the display escapes a keyword identifier (@event),
    // which no metadata name carries, so a lookup by that key would miss the type.
    internal static string ToNamespaceMetadataName(INamespaceSymbol namespaceSymbol)
    {
        List<string> names = new List<string>();
        for (INamespaceSymbol current = namespaceSymbol;
             current != null && !current.IsGlobalNamespace;
             current = current.ContainingNamespace)
        {
            names.Add(current.Name);
        }

        names.Reverse();
        return string.Join(".", names);
    }

    public static string ToParameterTypeFullName(IParameterSymbol parameterSymbol)
    {
        return SpellParameterType(parameterSymbol, typeParametersByPosition: false);
    }

    public static string ToCecilFullName(ITypeSymbol typeSymbol)
    {
        return SpellType(typeSymbol, typeParametersByPosition: false);
    }

    /// <summary>
    /// The parameter type spelling for matching a source member to its compiled counterpart:
    /// <see cref="ToParameterTypeFullName"/>, except that a type parameter is spelled by its
    /// position (see <see cref="ToSignatureComparisonName"/>).
    /// </summary>
    public static string ToParameterSignatureComparisonName(IParameterSymbol parameterSymbol)
    {
        return SpellParameterType(parameterSymbol, typeParametersByPosition: true);
    }

    /// <summary>
    /// The type spelling for matching a source member to its compiled counterpart:
    /// <see cref="ToCecilFullName"/>, except that a type parameter is spelled by its position as
    /// metadata records it, <c>!!n</c> for a method's and <c>!n</c> for a type's, counted across the
    /// containing types outer first. Never a key or a label: those keep the source spelling.
    /// </summary>
    // Why by position: the edited source may rename a type parameter (Echo<T> to Echo<U>) without
    // changing the signature, and a swap (Foo<T, U>(T, U) to Foo<U, T>(T, U)) changes it while
    // keeping every name, so only the position tells the two apart.
    public static string ToSignatureComparisonName(ITypeSymbol typeSymbol)
    {
        return SpellType(typeSymbol, typeParametersByPosition: true);
    }

    private static string SpellParameterType(IParameterSymbol parameterSymbol, bool typeParametersByPosition)
    {
        // Roslyn's IParameterSymbol.Type omits the byref wrapper; Cecil FullName uses a trailing '&'.
        string typeName = SpellType(parameterSymbol.Type, typeParametersByPosition);
        if (parameterSymbol.RefKind != RefKind.None)
        {
            return typeName + "&";
        }

        return typeName;
    }

    private static string SpellType(ITypeSymbol typeSymbol, bool typeParametersByPosition)
    {
        // Why here (not only the parameter top level): source `List<dynamic>` / `dynamic[]`
        // nest TypeKind.Dynamic while compiled metadata uses System.Object at the same depth.
        if (typeSymbol != null && typeSymbol.TypeKind == TypeKind.Dynamic)
        {
            return "System.Object";
        }

        if (typeSymbol is IPointerTypeSymbol pointerType)
        {
            return SpellType(pointerType.PointedAtType, typeParametersByPosition) + "*";
        }

        if (typeSymbol is IArrayTypeSymbol arrayType)
        {
            string elementName = SpellType(arrayType.ElementType, typeParametersByPosition);
            if (arrayType.Rank == 1)
            {
                return elementName + "[]";
            }

            // Cecil ArrayType.FullName for non-vector arrays uses "[0...,0...]" (lower bounds),
            // not the C# syntactic "[,]".
            string[] dimensionMarks = new string[arrayType.Rank];
            for (int index = 0; index < arrayType.Rank; index++)
            {
                dimensionMarks[index] = "0...";
            }

            return elementName + "[" + string.Join(",", dimensionMarks) + "]";
        }

        if (typeSymbol is INamedTypeSymbol namedType)
        {
            // Cecil nests constructed generics as Outer/Inner<all-args-outer-to-inner>, so collect
            // TypeArguments from the containment chain rather than only the leaf type.
            List<ITypeSymbol> typeArguments = new List<ITypeSymbol>();
            CollectConstructedTypeArgumentsOuterToInner(namedType, typeArguments);
            string head = ToMetadataName(namedType.OriginalDefinition);
            if (typeArguments.Count == 0)
            {
                return head;
            }

            string args = string.Join(
                ",",
                typeArguments.Select(argument => SpellType(argument, typeParametersByPosition)));
            return head + "<" + args + ">";
        }

        if (typeParametersByPosition && typeSymbol is ITypeParameterSymbol typeParameter)
        {
            return SpellTypeParameterPosition(typeParameter);
        }

        return typeSymbol.ToDisplayString(
            new SymbolDisplayFormat(
                typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces));
    }

    // Why the containing types' arities: metadata gives a nested type every outer type parameter
    // first, so Outer<T>.Inner<U> holds T as !0 and U as !1, and a position counted within the
    // declaring type alone would spell both !0.
    private static string SpellTypeParameterPosition(ITypeParameterSymbol typeParameter)
    {
        if (typeParameter.TypeParameterKind == TypeParameterKind.Method)
        {
            return "!!" + typeParameter.Ordinal.ToString(CultureInfo.InvariantCulture);
        }

        int position = typeParameter.Ordinal;
        for (INamedTypeSymbol outer = typeParameter.DeclaringType?.ContainingType;
             outer != null;
             outer = outer.ContainingType)
        {
            position += outer.Arity;
        }

        return "!" + position.ToString(CultureInfo.InvariantCulture);
    }

    private static void CollectConstructedTypeArgumentsOuterToInner(
        INamedTypeSymbol namedType,
        List<ITypeSymbol> typeArguments)
    {
        if (namedType.ContainingType != null)
        {
            CollectConstructedTypeArgumentsOuterToInner(namedType.ContainingType, typeArguments);
        }

        if (namedType.IsGenericType && !namedType.IsUnboundGenericType)
        {
            typeArguments.AddRange(namedType.TypeArguments);
        }
    }
}
