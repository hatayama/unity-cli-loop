using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

/// <summary>
/// Finds, among the calls and accesses of one body, the compiled members whose signatures name a
/// compiled type this run also declares from source, and the compiled types declaring them.
/// </summary>
/// <remarks>
/// Why: a compiled API keeps expecting the compiled copy of such a type, so a body handing it the
/// copy this run declares fails to bind. Passing the file that declares the API as well rebuilds
/// the API against the same copy, which is the recovery a skipped row can name.
/// </remarks>
internal static class CompiledSignatureSplitCollector
{
    internal static CompiledSignatureSplit Collect(
        SemanticModel semanticModel,
        SyntaxNode body,
        IReadOnlyList<TextSpan> bindingErrorSpans)
    {
        IAssemblySymbol sourceAssembly = semanticModel.Compilation.Assembly;
        SortedSet<string> splitTypes = new SortedSet<string>(StringComparer.Ordinal);
        SortedSet<string> declaringTypes = new SortedSet<string>(StringComparer.Ordinal);
        foreach (SyntaxNode node in body.DescendantNodesAndSelf())
        {
            // Why only uses an error touches: a compiled API elsewhere in the body that binds is
            // not what failed, and naming its file would send the reader after the wrong fix.
            if (!IsMemberUse(node) || !TouchesAny(node.Span, bindingErrorSpans))
            {
                continue;
            }

            foreach (ISymbol member in FindUsedMembers(semanticModel, node))
            {
                AddSplit(member, sourceAssembly, splitTypes, declaringTypes);
            }
        }

        return new CompiledSignatureSplit(new List<string>(splitTypes), new List<string>(declaringTypes));
    }

    private static bool IsMemberUse(SyntaxNode node)
    {
        return node is InvocationExpressionSyntax
            || node is MemberAccessExpressionSyntax
            || node is ElementAccessExpressionSyntax
            || node is BaseObjectCreationExpressionSyntax;
    }

    private static bool TouchesAny(TextSpan span, IReadOnlyList<TextSpan> errorSpans)
    {
        foreach (TextSpan errorSpan in errorSpans)
        {
            if (span.IntersectsWith(errorSpan))
            {
                return true;
            }
        }

        return false;
    }

    // A use that failed overload resolution binds to no symbol, so its candidates are where the
    // compiled signature is found.
    private static List<ISymbol> FindUsedMembers(SemanticModel semanticModel, SyntaxNode node)
    {
        SymbolInfo symbolInfo = semanticModel.GetSymbolInfo(node);
        List<ISymbol> members = new List<ISymbol>(symbolInfo.CandidateSymbols);
        if (symbolInfo.Symbol != null)
        {
            members.Add(symbolInfo.Symbol);
        }

        if (members.Count == 0 && node is MemberAccessExpressionSyntax memberAccess)
        {
            members.AddRange(FindExtensionsOnCompiledCopy(semanticModel, memberAccess));
        }

        return members;
    }

    // Why a lookup: a compiled extension whose receiver is the compiled copy leaves no symbol and
    // no candidate on a receiver of the copy this run declares (CS1929), so the extension is only
    // found by looking the name up on the compiled copy itself.
    private static IEnumerable<ISymbol> FindExtensionsOnCompiledCopy(
        SemanticModel semanticModel,
        MemberAccessExpressionSyntax memberAccess)
    {
        if (!(semanticModel.GetTypeInfo(memberAccess.Expression).Type is INamedTypeSymbol receiverType)
            || !IsFromSource(receiverType, semanticModel.Compilation.Assembly))
        {
            return Array.Empty<ISymbol>();
        }

        INamedTypeSymbol compiledCopy = FindCompiledCopy(semanticModel.Compilation, receiverType);
        if (compiledCopy == null)
        {
            return Array.Empty<ISymbol>();
        }

        return semanticModel.LookupSymbols(
            memberAccess.Name.SpanStart,
            compiledCopy,
            memberAccess.Name.Identifier.ValueText,
            includeReducedExtensionMethods: true);
    }

    private static INamedTypeSymbol FindCompiledCopy(Compilation compilation, INamedTypeSymbol sourceType)
    {
        string reflectionName = CecilTypeNames.ToMetadataName(sourceType.OriginalDefinition).Replace('/', '+');
        foreach (IAssemblySymbol referenced in compilation.SourceModule.ReferencedAssemblySymbols)
        {
            INamedTypeSymbol compiledCopy = referenced.GetTypeByMetadataName(reflectionName);
            if (compiledCopy != null)
            {
                return compiledCopy;
            }
        }

        return null;
    }

    private static void AddSplit(
        ISymbol member,
        IAssemblySymbol sourceAssembly,
        SortedSet<string> splitTypes,
        SortedSet<string> declaringTypes)
    {
        INamedTypeSymbol declaringType = member?.ContainingType;
        if (declaringType == null || IsFromSource(declaringType, sourceAssembly))
        {
            return;
        }

        List<INamedTypeSymbol> signatureTypes = new List<INamedTypeSymbol>();
        CollectSignatureTypes(member, signatureTypes);
        bool found = false;
        foreach (INamedTypeSymbol signatureType in signatureTypes)
        {
            // Only a type of the declaring type's own assembly can be rebuilt by passing the
            // declaring file: that file compiles into the same assembly as the copy it names.
            if (!SymbolEqualityComparer.Default.Equals(signatureType.ContainingAssembly, declaringType.ContainingAssembly)
                || !HasSourceCopy(signatureType, sourceAssembly))
            {
                continue;
            }

            splitTypes.Add(CecilTypeNames.ToMetadataName(signatureType.OriginalDefinition));
            found = true;
        }

        if (found)
        {
            declaringTypes.Add(CecilTypeNames.ToMetadataName(declaringType.OriginalDefinition));
        }
    }

    private static bool IsFromSource(INamedTypeSymbol type, IAssemblySymbol sourceAssembly)
    {
        return SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, sourceAssembly);
    }

    // Why '+': the source assembly looks nested types up by their reflection name, while the
    // name reported to the Editor keeps the metadata form Cecil reads.
    private static bool HasSourceCopy(INamedTypeSymbol type, IAssemblySymbol sourceAssembly)
    {
        string reflectionName = CecilTypeNames.ToMetadataName(type.OriginalDefinition).Replace('/', '+');
        return sourceAssembly.GetTypeByMetadataName(reflectionName) != null;
    }

    private static void CollectSignatureTypes(ISymbol member, List<INamedTypeSymbol> types)
    {
        switch (member)
        {
            case IMethodSymbol method:
                AddType(method.ReturnType, types);
                foreach (IParameterSymbol parameter in method.Parameters)
                {
                    AddType(parameter.Type, types);
                }

                foreach (ITypeSymbol typeArgument in method.TypeArguments)
                {
                    AddType(typeArgument, types);
                }

                // A reduced extension call drops the receiver from its parameters, and a split in
                // the receiver is exactly what makes the call fail to bind.
                if (method.ReducedFrom != null)
                {
                    CollectSignatureTypes(method.ReducedFrom, types);
                }

                return;
            case IPropertySymbol property:
                AddType(property.Type, types);
                foreach (IParameterSymbol parameter in property.Parameters)
                {
                    AddType(parameter.Type, types);
                }

                return;
            case IFieldSymbol field:
                AddType(field.Type, types);
                return;
            case IEventSymbol eventSymbol:
                AddType(eventSymbol.Type, types);
                return;
        }
    }

    // Walks type arguments and array elements, so the payload of Action<Payload> or Payload[] is
    // found the way the lambda's target type needs it.
    private static void AddType(ITypeSymbol type, List<INamedTypeSymbol> types)
    {
        if (type is IArrayTypeSymbol arrayType)
        {
            AddType(arrayType.ElementType, types);
            return;
        }

        if (!(type is INamedTypeSymbol namedType))
        {
            return;
        }

        types.Add(namedType);
        foreach (ITypeSymbol typeArgument in namedType.TypeArguments)
        {
            AddType(typeArgument, types);
        }
    }
}

/// <summary>
/// The compiled types a body's compiled signatures name while this run declares them from source,
/// and the compiled types declaring those signatures, both as sorted metadata names.
/// </summary>
internal sealed class CompiledSignatureSplit
{
    internal CompiledSignatureSplit(List<string> splitTypeMetadataNames, List<string> declaringTypeMetadataNames)
    {
        SplitTypeMetadataNames = splitTypeMetadataNames;
        DeclaringTypeMetadataNames = declaringTypeMetadataNames;
    }

    internal List<string> SplitTypeMetadataNames { get; }

    internal List<string> DeclaringTypeMetadataNames { get; }
}
