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

        return typeSymbol.ContainingNamespace.ToDisplayString() + "." + typeSymbol.MetadataName;
    }

    public static string ToParameterTypeFullName(IParameterSymbol parameterSymbol)
    {
        // Roslyn's IParameterSymbol.Type omits the byref wrapper; Cecil FullName uses a trailing '&'.
        string typeName = ToCecilFullName(parameterSymbol.Type);
        if (parameterSymbol.RefKind != RefKind.None)
        {
            return typeName + "&";
        }

        return typeName;
    }

    public static string ToCecilFullName(ITypeSymbol typeSymbol)
    {
        // Why here (not only the parameter top level): source `List<dynamic>` / `dynamic[]`
        // nest TypeKind.Dynamic while compiled metadata uses System.Object at the same depth.
        if (typeSymbol != null && typeSymbol.TypeKind == TypeKind.Dynamic)
        {
            return "System.Object";
        }

        if (typeSymbol is IPointerTypeSymbol pointerType)
        {
            return ToCecilFullName(pointerType.PointedAtType) + "*";
        }

        if (typeSymbol is IArrayTypeSymbol arrayType)
        {
            string elementName = ToCecilFullName(arrayType.ElementType);
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

            string args = string.Join(",", typeArguments.Select(ToCecilFullName));
            return head + "<" + args + ">";
        }

        return typeSymbol.ToDisplayString(
            new SymbolDisplayFormat(
                typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces));
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
