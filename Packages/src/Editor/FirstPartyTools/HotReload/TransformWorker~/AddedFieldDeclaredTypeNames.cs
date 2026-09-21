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

/// <summary>
/// What: names the declared type of an added field the way reflection spells it, so the Editor
/// can resolve the type and check a value against it without reading the edited source.
/// </summary>
/// <remarks>
/// Why assembly-qualified: the Editor holds no compilation, and a bare full name leaves it
/// guessing which loaded assembly declares the type. Why an empty result is a legitimate answer:
/// a type the worker cannot name (an open type parameter, a pointer, an error type) is a type the
/// Editor could not resolve either, and a caller is better told that than handed a name that
/// resolves to something else.
/// Why it holds the binding compilation and the type home: a type declared in a reloaded source
/// is bound from that source, so the compiler reports the worker's own throwaway compilation as
/// its assembly. The Editor can only resolve the assembly the type is compiled into.
/// Why it also holds the run's retained body-edit records: an introduced type whose body this
/// edit changed stays declared in the binding tree, so it is bound from source too, and the only
/// assembly that holds it is the artifact a previous reload loaded.
/// </remarks>
internal sealed class AddedFieldDeclaredTypeNames
{
    private readonly IAssemblySymbol _bindingAssembly;
    private readonly WorkerTypeHome _home;
    private readonly IReadOnlyList<WorkerRetainedBodyEditType> _retainedBodyEditTypes;
    private readonly IntroducedTypeArtifactMap _artifactMap;

    public AddedFieldDeclaredTypeNames(
        IAssemblySymbol bindingAssembly,
        WorkerTypeHome home,
        IReadOnlyList<WorkerRetainedBodyEditType> retainedBodyEditTypes,
        IntroducedTypeArtifactMap artifactMap)
    {
        Debug.Assert(bindingAssembly != null, "The binding compilation always has an assembly symbol.");
        Debug.Assert(home != null, "Every transform run builds a type home before emitting output.");
        Debug.Assert(retainedBodyEditTypes != null, "A run with no retained body edit passes an empty list.");
        Debug.Assert(artifactMap != null, "A run with no retained artifact passes the empty map.");
        _bindingAssembly = bindingAssembly;
        _home = home;
        _retainedBodyEditTypes = retainedBodyEditTypes;
        _artifactMap = artifactMap;
    }

    public string ToAssemblyQualifiedName(ITypeSymbol typeSymbol)
    {
        string reflectionName = BuildReflectionName(typeSymbol);
        if (string.IsNullOrEmpty(reflectionName))
        {
            return string.Empty;
        }

        string assemblyName = FindAssemblySimpleName(typeSymbol);
        if (string.IsNullOrEmpty(assemblyName))
        {
            return string.Empty;
        }

        return reflectionName + ", " + assemblyName;
    }

    // Reflection spells nested types with '+', keeps the arity suffix of a generic definition,
    // and lists the arguments of a constructed generic as bracketed assembly-qualified names.
    private string BuildReflectionName(ITypeSymbol typeSymbol)
    {
        if (typeSymbol == null || typeSymbol.TypeKind == TypeKind.Error)
        {
            return string.Empty;
        }

        if (typeSymbol is IArrayTypeSymbol arrayType)
        {
            string elementName = BuildReflectionName(arrayType.ElementType);
            if (string.IsNullOrEmpty(elementName))
            {
                return string.Empty;
            }

            return elementName + FormatArraySuffix(arrayType.Rank);
        }

        if (typeSymbol is INamedTypeSymbol namedType)
        {
            return BuildNamedReflectionName(namedType);
        }

        // Type parameters, pointers, function pointers and dynamic have no resolvable name here.
        return string.Empty;
    }

    private string BuildNamedReflectionName(INamedTypeSymbol namedType)
    {
        string head = CecilTypeNames.ToMetadataName(namedType.OriginalDefinition).Replace('/', '+');
        List<ITypeSymbol> typeArguments = new List<ITypeSymbol>();
        CollectConstructedTypeArgumentsOuterToInner(namedType, typeArguments);
        if (typeArguments.Count == 0)
        {
            return head;
        }

        List<string> qualifiedArguments = new List<string>(typeArguments.Count);
        foreach (ITypeSymbol argument in typeArguments)
        {
            string qualifiedArgument = ToAssemblyQualifiedName(argument);
            if (string.IsNullOrEmpty(qualifiedArgument))
            {
                return string.Empty;
            }

            qualifiedArguments.Add("[" + qualifiedArgument + "]");
        }

        return head + "[" + string.Join(",", qualifiedArguments) + "]";
    }

    // Reflection writes a vector array as "[]" and a rank-n array as n-1 commas in one bracket.
    private static string FormatArraySuffix(int rank)
    {
        if (rank <= 1)
        {
            return "[]";
        }

        return "[" + new string(',', rank - 1) + "]";
    }

    // An array is named by the assembly of its element type, and a constructed generic by the
    // assembly of its definition, so both walk down to the type that actually has one.
    private string FindAssemblySimpleName(ITypeSymbol typeSymbol)
    {
        ITypeSymbol current = typeSymbol;
        while (current is IArrayTypeSymbol arrayType)
        {
            current = arrayType.ElementType;
        }

        ITypeSymbol definition = current?.OriginalDefinition;
        IAssemblySymbol assembly = definition?.ContainingAssembly;
        if (assembly == null)
        {
            return string.Empty;
        }

        if (!SymbolEqualityComparer.Default.Equals(assembly, _bindingAssembly))
        {
            return assembly.Identity.Name;
        }

        return FindSourceBoundAssemblySimpleName(definition as INamedTypeSymbol);
    }

    // A source-bound type is named by its compiled counterpart, or by the artifact a retained
    // body-edit record says serves it. One with neither has no assembly the Editor can resolve it
    // from, so it is left unnamed rather than guessed.
    private string FindSourceBoundAssemblySimpleName(INamedTypeSymbol sourceType)
    {
        INamedTypeSymbol compiledType = _home.FindCompiledType(sourceType);
        if (compiledType != null)
        {
            return compiledType.ContainingAssembly.Identity.Name;
        }

        return FindRetainedArtifactAssemblySimpleName(sourceType);
    }

    private string FindRetainedArtifactAssemblySimpleName(INamedTypeSymbol sourceType)
    {
        if (sourceType == null)
        {
            return string.Empty;
        }

        string metadataName = CecilTypeNames.ToMetadataName(sourceType);
        foreach (WorkerRetainedBodyEditType bodyEditType in _retainedBodyEditTypes)
        {
            if (!string.Equals(bodyEditType.MetadataName, metadataName, StringComparison.Ordinal))
            {
                continue;
            }

            string artifactAssemblyName = _artifactMap.FindArtifactAssemblyName(
                bodyEditType.OriginalAssemblyName,
                bodyEditType.OriginalAssemblyMvid,
                bodyEditType.MetadataName);
            return artifactAssemblyName ?? string.Empty;
        }

        return string.Empty;
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
