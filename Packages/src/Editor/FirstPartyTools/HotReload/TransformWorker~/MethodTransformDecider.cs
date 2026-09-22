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
using io.github.hatayama.UnityCliLoop.FirstPartyTools;

internal static class MethodTransformDecider
{
    // methodDeclaration may be null for property getters (bodyNode must still be in the bound tree).
    internal static MethodTransformDecision DecideMethodTransform(
        TypeDeclarationSyntax typeDeclaration,
        INamedTypeSymbol typeSymbol,
        MethodDeclarationSyntax methodDeclaration,
        IMethodSymbol methodSymbol,
        SyntaxNode bodyNode,
        SemanticModel semanticModel,
        INamedTypeSymbol compiledType,
        AddedMemberAccessLookup addedMemberAccess)
    {
        WorkerReason hardSkip = EvaluateHardSkipReason(
            typeDeclaration,
            typeSymbol,
            methodDeclaration,
            methodSymbol);
        if (hardSkip != null)
        {
            return MethodTransformDecision.Skip(hardSkip);
        }

        if (bodyNode == null)
        {
            return MethodTransformDecision.Skip(WorkerReason.Of(HotReloadWorkerReasonCode.MethodTransformNoBody));
        }

        if (ContainsBaseExpression(bodyNode))
        {
            return MethodTransformDecision.Skip(WorkerReason.Of(HotReloadWorkerReasonCode.MethodTransformBaseMemberCall));
        }

        WorkerReason eventUseReason = EventAccessorRules.EvaluateEventUseSkipReason(
            bodyNode,
            semanticModel,
            compiledType);
        if (eventUseReason != null)
        {
            return MethodTransformDecision.Skip(eventUseReason);
        }

        bool closureInaccessible = InaccessibleAccessScanner.SubtreeHasInaccessibleMemberAccess(
            semanticModel,
            FindClosureBodies(bodyNode));
        bool asyncIteratorInaccessible = IsAsyncOrIterator(methodDeclaration, bodyNode)
            && InaccessibleAccessScanner.SubtreeHasInaccessibleMemberAccess(semanticModel, new[] { bodyNode });
        // Why delegation is forced: a transplanted shim body still has to compile as C#, and C#
        // rejects raising or reading an event outside its declaring type whatever its visibility.
        bool eventAccessorsRequired = EventAccessorRules.BodyRequiresEventAccessors(bodyNode, semanticModel);

        if (!closureInaccessible && !asyncIteratorInaccessible && !eventAccessorsRequired)
        {
            return MethodTransformDecision.Transplant();
        }

        // Condition (a): only the private-access skip reasons are eligible for accessor rewrite.
        HotReloadWorkerReasonCode? rescuableSkipCode =
            BuildAccessorRescueReason(closureInaccessible, asyncIteratorInaccessible);

        if (!AccessorEligibility.TryBuildPlan(
                semanticModel,
                methodSymbol,
                typeSymbol,
                bodyNode,
                addedMemberAccess,
                out AccessorPlan feasibilityPlan,
                out WorkerReason accessorRejectReason))
        {
            return MethodTransformDecision.Skip(
                WorkerReason.Composite(
                    rescuableSkipCode ?? HotReloadWorkerReasonCode.EventAccessorRewriteUnavailable,
                    accessorRejectReason));
        }

        // Safety net: detection said "needs accessors" but eligibility found nothing to rewrite
        // (e.g. local-function-only async body). Transplant is correct — the body is unchanged.
        if (feasibilityPlan.Entries.Count == 0 && !eventAccessorsRequired)
        {
            return MethodTransformDecision.Transplant();
        }

        return MethodTransformDecision.Delegation();
    }

    // Null when the body needs accessors only for its event uses: there is no skip to rescue.
    private static HotReloadWorkerReasonCode? BuildAccessorRescueReason(
        bool closureInaccessible,
        bool asyncIteratorInaccessible)
    {
        if (closureInaccessible)
        {
            return HotReloadWorkerReasonCode.MethodTransformClosureInaccessibleAccess;
        }

        if (asyncIteratorInaccessible)
        {
            return HotReloadWorkerReasonCode.MethodTransformAsyncIteratorInaccessibleAccess;
        }

        return null;
    }

    internal static WorkerReason EvaluateHardSkipReason(
        TypeDeclarationSyntax typeDeclaration,
        INamedTypeSymbol typeSymbol,
        MethodDeclarationSyntax methodDeclaration,
        IMethodSymbol methodSymbol)
    {
        // A nested type inside a partial outer type still has an incomplete single-file model.
        for (TypeDeclarationSyntax declaration = typeDeclaration;
             declaration != null;
             declaration = declaration.Parent as TypeDeclarationSyntax)
        {
            if (declaration.Modifiers.Any(static modifier => modifier.IsKind(SyntaxKind.PartialKeyword)))
            {
                return WorkerReason.Of(HotReloadWorkerReasonCode.MethodTransformPartialType);
            }
        }

        if (typeSymbol.TypeKind == TypeKind.Struct || typeSymbol.IsValueType)
        {
            return WorkerReason.Of(HotReloadWorkerReasonCode.MethodTransformStructHost);
        }

        bool hasTypeParameters = methodDeclaration != null && methodDeclaration.TypeParameterList != null;
        if (typeSymbol.IsGenericType || methodSymbol.IsGenericMethod || hasTypeParameters)
        {
            return WorkerReason.Of(HotReloadWorkerReasonCode.MethodTransformGenericMethodOrType);
        }

        // Explicit interface implementations have dotted metadata names (e.g. IFoo.Bar) that are
        // not valid C# identifiers for shim method names; sanitizing would also desync the
        // matcher (Cecil MethodDefinition.Name). They are skipped with an explicit reason.
        if (methodDeclaration != null && methodDeclaration.ExplicitInterfaceSpecifier != null)
        {
            return WorkerReason.Of(HotReloadWorkerReasonCode.MethodTransformExplicitInterfaceImplementation);
        }

        return null;
    }

    internal static bool ContainsBaseExpression(SyntaxNode bodyNode)
    {
        return bodyNode.DescendantNodes().OfType<BaseExpressionSyntax>().Any();
    }

    internal static bool IsAsyncOrIterator(MethodDeclarationSyntax methodDeclaration, SyntaxNode bodyNode)
    {
        if (methodDeclaration != null
            && methodDeclaration.Modifiers.Any(static modifier => modifier.IsKind(SyntaxKind.AsyncKeyword)))
        {
            return true;
        }

        // Yields inside local functions do not make the outer method an iterator.
        foreach (YieldStatementSyntax yieldStatement in bodyNode.DescendantNodes().OfType<YieldStatementSyntax>())
        {
            if (!IsInsideLocalFunction(yieldStatement, bodyNode))
            {
                return true;
            }
        }

        return false;
    }

    internal static bool IsInsideLocalFunction(SyntaxNode node, SyntaxNode stopAt)
    {
        for (SyntaxNode current = node.Parent; current != null && current != stopAt; current = current.Parent)
        {
            if (current is LocalFunctionStatementSyntax)
            {
                return true;
            }
        }

        return false;
    }

    internal static List<SyntaxNode> FindClosureBodies(SyntaxNode bodyNode)
    {
        List<SyntaxNode> bodies = new List<SyntaxNode>();
        foreach (SyntaxNode node in bodyNode.DescendantNodes())
        {
            if (node is SimpleLambdaExpressionSyntax simpleLambda)
            {
                bodies.Add(simpleLambda.Body);
            }
            else if (node is ParenthesizedLambdaExpressionSyntax parenthesizedLambda)
            {
                bodies.Add(parenthesizedLambda.Body);
            }
            else if (node is AnonymousMethodExpressionSyntax anonymousMethod && anonymousMethod.Body != null)
            {
                bodies.Add(anonymousMethod.Body);
            }
            else if (node is LocalFunctionStatementSyntax localFunction)
            {
                SyntaxNode localBody = (SyntaxNode)localFunction.Body ?? localFunction.ExpressionBody;
                if (localBody != null)
                {
                    bodies.Add(localBody);
                }
            }
            else if (node is QueryExpressionSyntax queryExpression)
            {
                // Query clauses compile to display-class methods that JIT normally; treat the
                // whole query (including the source expression) as a closure body.
                bodies.Add(queryExpression);
            }
        }

        return bodies;
    }

    // Why name the compiled types: when the body fails because a compiled API still takes the
    // compiled copy of a type this run declares, passing the API's file as well is a recovery
    // short of a compile, and only the declaring types tell the reader which file that is.
    private static WorkerReason DescribeUnboundBody(
        SemanticModel semanticModel,
        SyntaxNode methodBodyNode,
        Diagnostic bindingError,
        IntroducedTypeArtifactMap artifactMap)
    {
        string diagnosticText = bindingError.Id + ": " + bindingError.GetMessage(CultureInfo.InvariantCulture);
        CompiledSignatureSplit split = CompiledSignatureSplitCollector.Collect(
            semanticModel,
            methodBodyNode,
            AddedMemberBindingGuard.FindBindingErrorSpans(semanticModel, methodBodyNode),
            artifactMap);
        // Checked first: a compile clears this split and any other one, while the advice to
        // pass a file would leave this one in place.
        if (split.ArtifactHostMetadataNames.Count > 0)
        {
            return WorkerReason.NamingCompiledTypes(
                HotReloadWorkerReasonCode.AddedMethodCallsIntroducedMemberBoundToCompiledType,
                split.ArtifactBoundTypeMetadataNames.ToArray(),
                diagnosticText,
                QuoteNames(split.ArtifactHostMetadataNames),
                QuoteNames(split.ArtifactBoundTypeMetadataNames));
        }

        if (split.DeclaringTypeMetadataNames.Count == 0)
        {
            return WorkerReason.Of(HotReloadWorkerReasonCode.AddedMethodBodyUnbound, diagnosticText);
        }

        return WorkerReason.NamingCompiledTypes(
            HotReloadWorkerReasonCode.AddedMethodBodyBindsCompiledSignature,
            split.DeclaringTypeMetadataNames.ToArray(),
            diagnosticText,
            QuoteNames(split.SplitTypeMetadataNames),
            QuoteNames(split.DeclaringTypeMetadataNames));
    }

    private static string QuoteNames(List<string> names)
    {
        return "'" + string.Join("', '", names) + "'";
    }

    // Why a second plan pass: DecideMethodTransform only sets UsesDelegation for
    // async/iterator/closure bodies. An ordinary added method JIT-compiles in the
    // shim assembly, so inaccessible compiled members must take the same accessor
    // rewrite or be Skipped — Success plus a raw FieldAccessException is the FB bug.
    internal static MethodTransformDecision DecideAddedMethodAccessors(
        IMethodSymbol methodSymbol,
        INamedTypeSymbol typeSymbol,
        SyntaxNode methodBodyNode,
        SemanticModel semanticModel,
        MethodTransformDecision current,
        AddedMemberAccessLookup addedMemberAccess,
        IntroducedTypeArtifactMap artifactMap)
    {
        // Checked before the delegation path: a closure that binds one private access still takes
        // that path, and an unbound call beside it would reach the shim unrewritten.
        Diagnostic bindingError = AddedMemberBindingGuard.FindFirstBindingError(semanticModel, methodBodyNode);
        if (bindingError != null)
        {
            return MethodTransformDecision.Skip(
                DescribeUnboundBody(semanticModel, methodBodyNode, bindingError, artifactMap));
        }

        if (current.UsesDelegation)
        {
            return MethodTransformDecision.AddedMethod(true);
        }

        if (!InaccessibleAccessScanner.SubtreeHasInaccessibleMemberAccess(semanticModel, new[] { methodBodyNode }))
        {
            return MethodTransformDecision.AddedMethod(false);
        }

        if (!AccessorEligibility.TryBuildPlan(
                semanticModel,
                methodSymbol,
                typeSymbol,
                methodBodyNode,
                addedMemberAccess,
                out AccessorPlan feasibilityPlan,
                out WorkerReason accessorRejectReason))
        {
            return MethodTransformDecision.Skip(
                WorkerReason.Composite(
                    HotReloadWorkerReasonCode.AddedMethodInaccessibleAccessNoRewrite,
                    accessorRejectReason));
        }

        bool usesDelegation = feasibilityPlan.Entries.Count > 0;
        return MethodTransformDecision.AddedMethod(usesDelegation);
    }

    internal static WorkerReason EvaluateAddedMethodSkipReason(
        IMethodSymbol methodSymbol,
        MethodDeclarationSyntax methodDeclaration)
    {
        if (methodSymbol.IsAbstract || methodSymbol.IsVirtual || methodSymbol.IsOverride)
        {
            return WorkerReason.Of(HotReloadWorkerReasonCode.AddedMethodVirtualOrAbstract);
        }

        bool hasTypeParameters = methodDeclaration != null && methodDeclaration.TypeParameterList != null;
        if (methodSymbol.IsGenericMethod || hasTypeParameters)
        {
            return WorkerReason.Of(HotReloadWorkerReasonCode.AddedMethodGeneric);
        }

        return null;
    }
}
