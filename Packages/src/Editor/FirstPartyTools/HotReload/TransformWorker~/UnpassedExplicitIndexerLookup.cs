using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

/// <summary>
/// Finds an Index or Range access, inside a method body, that the worker binds through the implicit
/// Index or Range support (Length or Count with the int indexer, or Slice) while the compiled type
/// it reads, declared in a file the run does not pass, also declares a non-public indexer taking
/// that Index or Range. The compiler prefers such an indexer, so the shim compile calls it.
/// </summary>
/// <remarks>
/// Why no binding error points at it: the worker reads the target assembly through its public
/// surface only, so the non-public indexer is not there and the implicit support binds without an
/// error. The shim compile references a copy with every member public, binds the indexer instead,
/// and a closure or state machine of the shim assembly then throws MethodAccessException calling it.
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
        if (targetAssembly == null)
        {
            yield break;
        }

        foreach (SyntaxNode node in bodyNode.DescendantNodesAndSelf())
        {
            if (!(node is ElementAccessExpressionSyntax) && !(node is ElementBindingExpressionSyntax))
            {
                continue;
            }

            string argumentType = UnpassedInternalAccessorLookup.IndexOrRangeArgumentTypeOrNull(semanticModel, node);
            if (argumentType == null
                || semanticModel.GetOperation(node) is not IImplicitIndexerReferenceOperation implicitIndexer
                || implicitIndexer.Instance?.Type is not INamedTypeSymbol receiver)
            {
                continue;
            }

            IMethodSymbol accessor = FindHiddenIndexerAccessorOrNull(
                receiver,
                argumentType,
                (ExpressionSyntax)node,
                semanticModel,
                targetAssembly);
            if (accessor == null)
            {
                continue;
            }

            UnpassedInternalMemberUse use = UnpassedInternalMemberUse.ForImplicitCall(
                accessor,
                node,
                bodyNode,
                methodDeclarationOrNull,
                decision,
                semanticModel);
            yield return (use, DescribeEvidence(argumentType));
        }
    }

    // Why the first indexer found from the receiver up: the compiler looks the indexer up on the
    // receiver and its base types, and a type that declares one with the argument's type hides the
    // implicit support. Why non-public only: a public one is on the surface the worker reads, so
    // the worker would have bound to it instead of the implicit support.
    private static IMethodSymbol FindHiddenIndexerAccessorOrNull(
        INamedTypeSymbol receiver,
        string argumentType,
        ExpressionSyntax access,
        SemanticModel semanticModel,
        IAssemblySymbol targetAssembly)
    {
        foreach (INamedTypeSymbol type in UnpassedInternalMemberUse.EnumerateUnpassedCompiledTypes(receiver, semanticModel, targetAssembly))
        {
            IPropertySymbol indexer = type.GetMembers(WellKnownMemberNames.Indexer)
                .OfType<IPropertySymbol>()
                .FirstOrDefault(property => TakesOnly(property, argumentType));
            if (indexer == null)
            {
                continue;
            }

            if (indexer.DeclaredAccessibility == Accessibility.Public)
            {
                return null;
            }

            return AccessorCalledOrNull(indexer, access);
        }

        return null;
    }

    private static bool TakesOnly(IPropertySymbol indexer, string argumentType)
    {
        return indexer.Parameters.Length == 1 && indexer.Parameters[0].Type.ToDisplayString() == argumentType;
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

    private static string DescribeEvidence(string argumentType)
    {
        Debug.Assert(!string.IsNullOrEmpty(argumentType), "argumentType must not be null or empty.");
        return argumentType == RangeTypeName
            ? "the compiled type declares this[System.Range], which the compiler calls instead of Slice"
            : "the compiled type declares this[System.Index], which the compiler calls instead of the int indexer";
    }
}
