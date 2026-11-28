using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using io.github.hatayama.UnityCliLoop.FirstPartyTools;

/// <summary>
/// What: decides how an existing or added ordinary method of an edited type is transformed: the
/// early skips, the method transform itself, the checks that may skip a body that does not bind,
/// and the reason a skipped body reports.
/// </summary>
internal static class OrdinaryMethodTransformDecider
{
    internal static MethodTransformDecision Decide(
        bool isAddedMethod,
        MethodDeclarationSyntax methodDeclaration,
        IMethodSymbol methodSymbol,
        TypeEmitState typeState,
        SemanticModel semanticModel)
    {
        SyntaxNode methodBodyNode =
            (SyntaxNode)methodDeclaration.Body ?? methodDeclaration.ExpressionBody;
        WorkerReason addedSkip = isAddedMethod
            ? MethodTransformDecider.EvaluateAddedMethodSkipReason(methodSymbol, methodDeclaration)
            : null;
        WorkerReason earlySkip = addedSkip
            ?? BaseCallRules.EvaluateHostSkipReason(
                isAddedMethod,
                typeState.HomeAssemblyName != null,
                methodBodyNode,
                semanticModel);
        MethodTransformDecision decision = earlySkip != null
            ? MethodTransformDecision.Skip(earlySkip)
            : MethodTransformDecider.DecideMethodTransform(
                typeState.TypeDeclaration,
                typeState.TypeSymbol,
                methodDeclaration,
                methodSymbol,
                methodBodyNode,
                semanticModel,
                typeState.CompiledType,
                typeState.AddedMemberAccess,
                typeState.AddedEvents,
                typeState.SourceUnit.PartialTypeParts,
                typeState.TargetAssembly,
                typeState.SourceUnit.PatchBinding);
        if (isAddedMethod && decision.SkipReason == null)
        {
            decision = MethodTransformDecider.DecideAddedMethodAccessors(
                methodSymbol,
                typeState.TypeSymbol,
                methodBodyNode,
                semanticModel,
                decision,
                typeState.AddedMemberAccess,
                typeState.SourceUnit,
                typeState.TargetAssembly);
        }

        if (!isAddedMethod && decision.SkipReason == null && typeState.SourceUnit.Input.ReappliedSibling)
        {
            WorkerReason siblingSkip = ReappliedSiblingBodyGuard.DescribeSkipOrNull(
                semanticModel,
                methodBodyNode,
                typeState.TargetAssembly,
                methodDeclaration,
                decision,
                typeState.TypeSymbol,
                typeState.SourceUnit.PatchBinding);
            if (siblingSkip != null)
            {
                decision = MethodTransformDecision.Skip(siblingSkip);
            }
        }

        // Why existing methods only: an added method's unbound body is already skipped by the
        // added-member binding check, with its own reason.
        if (!isAddedMethod && decision.SkipReason == null)
        {
            WorkerReason unresolvedNameSkip = UnresolvedBodyNameGuard.DescribeSkipOrNull(
                typeState.TypeDeclaration,
                semanticModel,
                methodBodyNode,
                methodDeclaration,
                decision,
                typeState.TypeSymbol,
                typeState.TargetAssembly,
                typeState.SourceUnit.PatchBinding);
            if (unresolvedNameSkip != null)
            {
                decision = MethodTransformDecision.Skip(unresolvedNameSkip);
            }
        }

        return isAddedMethod
            ? decision
            : PreferUnresolvedNameReason(decision, methodDeclaration, methodBodyNode, typeState, semanticModel);
    }

    // Why only the reason moves: the body names a member Unity's compiler rejects there, and the
    // unresolved-name reason says why better than the hidden-member one when it applies; without
    // one the body keeps the hidden-member skip. Added methods keep it too: their unbound body is
    // reported by the added-member binding check, not by the unresolved-name check.
    private static MethodTransformDecision PreferUnresolvedNameReason(
        MethodTransformDecision decision,
        MethodDeclarationSyntax methodDeclaration,
        SyntaxNode methodBodyNode,
        TypeEmitState typeState,
        SemanticModel semanticModel)
    {
        if (decision.ShapeForUnresolvedNameCheck == null)
        {
            return decision;
        }

        WorkerReason unresolvedNameReason = UnresolvedBodyNameGuard.DescribeSkipOrNull(
            typeState.TypeDeclaration,
            semanticModel,
            methodBodyNode,
            methodDeclaration,
            decision.ShapeForUnresolvedNameCheck,
            typeState.TypeSymbol,
            typeState.TargetAssembly,
            typeState.SourceUnit.PatchBinding);
        return unresolvedNameReason == null ? decision : MethodTransformDecision.Skip(unresolvedNameReason);
    }
}
