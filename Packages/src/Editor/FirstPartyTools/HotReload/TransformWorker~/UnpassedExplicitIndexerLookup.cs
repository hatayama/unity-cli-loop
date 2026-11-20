using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

/// <summary>
/// Finds an Index or Range access, inside a method body, that the worker binds through the implicit
/// Index or Range support (Length or Count with the int indexer, or Slice) while a compiled type
/// the access looks the indexer up on, declared in a file the run does not pass, also declares a
/// non-public indexer that accepts that Index or Range. The compiler prefers such an indexer, so
/// the shim compile calls it.
/// </summary>
/// <remarks>
/// Why no binding error points at it: the worker reads the target assembly through its public
/// surface only, so the non-public indexer is not there and the implicit support binds without an
/// error. The shim compile references a copy with every member public, binds the indexer instead,
/// and code of the shim assembly then throws MethodAccessException calling it.
/// </remarks>
internal static class UnpassedExplicitIndexerLookup
{
    private const string RangeTypeName = "System.Range";

    /// <summary>
    /// Every such access in <paramref name="bodyNode"/>, in source order, each with the evidence
    /// that says which indexer the compiler calls and what the worker bound instead. Why every one:
    /// a use the patched method itself makes is within reach, and says nothing about a later one
    /// inside a closure.
    /// </summary>
    internal static IEnumerable<(UnpassedInternalMemberUse Use, string Evidence)> FindAll(
        SemanticModel semanticModel,
        SyntaxNode bodyNode,
        MethodDeclarationSyntax methodDeclarationOrNull,
        MethodTransformDecision decision,
        IAssemblySymbol targetAssembly)
    {
        foreach (HiddenIndexerAccess access in EnumerateAccesses(semanticModel, bodyNode, targetAssembly))
        {
            UnpassedInternalMemberUse use = UnpassedInternalMemberUse.ForImplicitCall(
                access.Accessor,
                access.Node,
                bodyNode,
                methodDeclarationOrNull,
                decision,
                semanticModel);
            yield return (use, access.Evidence);
        }
    }

    /// <summary>
    /// The accessor the first such access in <paramref name="bodyNode"/> calls, or null when there
    /// is none. For an added method, which runs in the shim assembly from its first statement, so
    /// any such access fails wherever it is written.
    /// </summary>
    internal static IMethodSymbol FindFirstAccessorOrNull(
        SemanticModel semanticModel,
        SyntaxNode bodyNode,
        IAssemblySymbol targetAssembly)
    {
        return EnumerateAccesses(semanticModel, bodyNode, targetAssembly)
            .Select(access => access.Accessor)
            .FirstOrDefault();
    }

    private static IEnumerable<HiddenIndexerAccess> EnumerateAccesses(
        SemanticModel semanticModel,
        SyntaxNode bodyNode,
        IAssemblySymbol targetAssembly)
    {
        if (targetAssembly == null || bodyNode == null)
        {
            yield break;
        }

        foreach (SyntaxNode node in bodyNode.DescendantNodesAndSelf())
        {
            if (!(node is ElementAccessExpressionSyntax) && !(node is ElementBindingExpressionSyntax))
            {
                continue;
            }

            string argumentTypeName = UnpassedInternalAccessorLookup.IndexOrRangeArgumentTypeOrNull(semanticModel, node);
            if (argumentTypeName == null
                || semanticModel.GetOperation(node) is not IImplicitIndexerReferenceOperation implicitIndexer
                || implicitIndexer.Instance?.Type == null
                || implicitIndexer.Argument?.Type == null)
            {
                continue;
            }

            IPropertySymbol indexer = FindHiddenIndexerOrNull(
                implicitIndexer.Instance.Type,
                implicitIndexer.Argument.Type,
                semanticModel,
                targetAssembly);
            IMethodSymbol accessor = indexer == null ? null : AccessorCalledOrNull(indexer, (ExpressionSyntax)node);
            if (accessor == null)
            {
                continue;
            }

            yield return new HiddenIndexerAccess(node, accessor, DescribeEvidence(indexer, argumentTypeName));
        }
    }

    // Why every type the lookup reaches and not only the receiver's: the compiler looks the indexer
    // up on the receiver's type and its base types, or on the constraint types of a type parameter.
    // Why any non-public one that accepts the argument: no public one does, or the worker would have
    // bound it instead of the implicit support, so the shim compile's overload resolution picks
    // among the non-public ones.
    private static IPropertySymbol FindHiddenIndexerOrNull(
        ITypeSymbol receiverType,
        ITypeSymbol argumentType,
        SemanticModel semanticModel,
        IAssemblySymbol targetAssembly)
    {
        foreach (INamedTypeSymbol lookupType in LookupTypes(receiverType))
        {
            INamedTypeSymbol compiled = UnpassedCompiledCopyOrNull(lookupType, semanticModel, targetAssembly);
            if (compiled == null)
            {
                continue;
            }

            IPropertySymbol indexer = compiled.GetMembers(WellKnownMemberNames.Indexer)
                .OfType<IPropertySymbol>()
                .FirstOrDefault(property => property.DeclaredAccessibility != Accessibility.Public
                    && UnpassedIndexerApplicability.Accepts(property, lookupType, argumentType));
            if (indexer != null)
            {
                return indexer;
            }
        }

        return null;
    }

    private static IEnumerable<INamedTypeSymbol> LookupTypes(ITypeSymbol receiverType)
    {
        if (receiverType is ITypeParameterSymbol typeParameter)
        {
            foreach (ITypeSymbol constraint in typeParameter.ConstraintTypes)
            {
                foreach (INamedTypeSymbol type in LookupTypes(constraint))
                {
                    yield return type;
                }
            }

            yield break;
        }

        for (INamedTypeSymbol type = receiverType as INamedTypeSymbol; type != null; type = type.BaseType)
        {
            yield return type;
        }
    }

    // The all-members copy of the type itself, or null when the run declares it or it is not a type
    // of the target assembly. Why compared by metadata name: the copy lives in another compilation,
    // so its symbol never equals the worker's.
    private static INamedTypeSymbol UnpassedCompiledCopyOrNull(
        INamedTypeSymbol type,
        SemanticModel semanticModel,
        IAssemblySymbol targetAssembly)
    {
        INamedTypeSymbol compiled = UnpassedInternalMemberUse
            .EnumerateUnpassedCompiledTypes(type, semanticModel, targetAssembly)
            .FirstOrDefault();
        return compiled != null && UnpassedIndexerApplicability.HaveTheSameMetadataName(compiled, type.OriginalDefinition)
            ? compiled
            : null;
    }

    // Why the getter first: a compound assignment or an increment calls both, and the reason then
    // names the getter, as it does for an internal accessor.
    private static IMethodSymbol AccessorCalledOrNull(IPropertySymbol indexer, ExpressionSyntax access)
    {
        (bool reads, bool writes) = UnpassedInternalAccessorLookup.AccessorsCalledBy(access);
        if (reads && indexer.GetMethod != null)
        {
            return indexer.GetMethod;
        }

        return writes ? AssignmentTargetRules.AccessorCalledByWrite(indexer) : null;
    }

    private static string DescribeEvidence(IPropertySymbol indexer, string argumentTypeName)
    {
        Debug.Assert(indexer != null, "indexer must not be null.");
        string declared = "this[" + string.Join(", ", indexer.Parameters.Select(parameter => parameter.Type.ToDisplayString())) + "]";
        string replaced = argumentTypeName == RangeTypeName ? "Slice" : "the int indexer";
        return "the compiled type declares " + declared + ", which the compiler calls instead of " + replaced;
    }

    private readonly struct HiddenIndexerAccess
    {
        internal HiddenIndexerAccess(SyntaxNode node, IMethodSymbol accessor, string evidence)
        {
            Node = node;
            Accessor = accessor;
            Evidence = evidence;
        }

        internal SyntaxNode Node { get; }

        internal IMethodSymbol Accessor { get; }

        internal string Evidence { get; }
    }
}
