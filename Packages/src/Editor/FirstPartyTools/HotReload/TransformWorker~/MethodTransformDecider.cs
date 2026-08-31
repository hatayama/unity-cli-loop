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

internal static class MethodTransformDecider
{
    // methodDeclaration may be null for property getters (bodyNode must still be in the bound tree).
    internal static MethodTransformDecision DecideMethodTransform(
        TypeDeclarationSyntax typeDeclaration,
        INamedTypeSymbol typeSymbol,
        MethodDeclarationSyntax methodDeclaration,
        IMethodSymbol methodSymbol,
        SyntaxNode bodyNode,
        SemanticModel semanticModel)
    {
        string hardSkip = EvaluateHardSkipReason(
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
            return MethodTransformDecision.Skip("Methods without a body (abstract/extern) are skipped.");
        }

        if (ContainsBaseExpression(bodyNode))
        {
            return MethodTransformDecision.Skip(
                "Methods that call base. members are skipped; C# cannot express base calls outside the type.");
        }

        string eventUseReason = EvaluateEventUseSkipReason(bodyNode, semanticModel);
        if (eventUseReason != null)
        {
            return MethodTransformDecision.Skip(eventUseReason);
        }

        bool closureInaccessible = InaccessibleAccessScanner.SubtreeHasInaccessibleMemberAccess(
            semanticModel,
            FindClosureBodies(bodyNode));
        bool asyncIteratorInaccessible = IsAsyncOrIterator(methodDeclaration, bodyNode)
            && InaccessibleAccessScanner.SubtreeHasInaccessibleMemberAccess(semanticModel, new[] { bodyNode });

        if (!closureInaccessible && !asyncIteratorInaccessible)
        {
            return MethodTransformDecision.Transplant();
        }

        // Condition (a): only the v1 private-access skip reasons are eligible for accessor rewrite.
        string v1Reason = closureInaccessible
            ? "Lambda, local-function, or query-expression bodies that access private/internal members "
                + "are skipped in v1 (closure methods JIT-compile normally and fail accessibility checks)."
            : "Async or iterator methods whose bodies access private/internal members are skipped in v1 "
                + "(state-machine MoveNext JIT-compiles normally and fails accessibility checks).";

        if (!AccessorEligibility.TryBuildPlan(
                semanticModel,
                methodSymbol,
                typeSymbol,
                bodyNode,
                out AccessorPlan feasibilityPlan,
                out string accessorRejectReason))
        {
            return MethodTransformDecision.Skip(
                v1Reason + " Accessor rewrite unavailable: " + accessorRejectReason);
        }

        // Safety net: detection said "needs accessors" but eligibility found nothing to rewrite
        // (e.g. local-function-only async body). Transplant is correct — the body is unchanged.
        if (feasibilityPlan.Entries.Count == 0)
        {
            return MethodTransformDecision.Transplant();
        }

        return MethodTransformDecision.Delegation();
    }

    internal static string EvaluateHardSkipReason(
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
                return "Partial types are skipped because a single file cannot provide a complete semantic model.";
            }
        }

        if (typeSymbol.TypeKind == TypeKind.Struct || typeSymbol.IsValueType)
        {
            return "Struct (value type) methods are out of scope for v1; byref instance transplant is unverified.";
        }

        bool hasTypeParameters = methodDeclaration != null && methodDeclaration.TypeParameterList != null;
        if (typeSymbol.IsGenericType || methodSymbol.IsGenericMethod || hasTypeParameters)
        {
            return "Generic methods and methods inside generic types cannot be safely patched with Harmony. Run 'uloop compile'.";
        }

        // Explicit interface implementations have dotted metadata names (e.g. IFoo.Bar) that are
        // not valid C# identifiers for shim method names; sanitizing would also desync the
        // matcher (Cecil MethodDefinition.Name). v1 skips them with an explicit reason.
        if (methodDeclaration != null && methodDeclaration.ExplicitInterfaceSpecifier != null)
        {
            return "Explicit interface implementations are skipped in v1.";
        }

        return null;
    }

    // Why skip event uses beyond +=/-=: outside the declaring type C# only allows those
    // assignments, so a shim cannot compile Raise/Invoke/read. nameof(ScoreChanged) and
    // similar non-runtime references are also skipped — Skip is an honest report and safer
    // than a compile failure.
    internal static string EvaluateEventUseSkipReason(SyntaxNode bodyNode, SemanticModel semanticModel)
    {
        foreach (SyntaxNode node in bodyNode.DescendantNodesAndSelf())
        {
            if (node is not IdentifierNameSyntax && node is not MemberAccessExpressionSyntax)
            {
                continue;
            }

            IEventSymbol eventSymbol = semanticModel.GetSymbolInfo(node).Symbol as IEventSymbol;
            if (eventSymbol == null)
            {
                continue;
            }

            // this.E / instance.E resolve the same event on the IdentifierName and the outer
            // MemberAccess; judge usage on the outer expression only.
            SyntaxNode effective = node;
            if (node.Parent is MemberAccessExpressionSyntax parentAccess && parentAccess.Name == node)
            {
                effective = parentAccess;
            }

            // += / -= on the left-hand side are the only event operations C# allows outside the
            // declaring type.
            if (effective.Parent is AssignmentExpressionSyntax assignment
                && (assignment.IsKind(SyntaxKind.AddAssignmentExpression)
                    || assignment.IsKind(SyntaxKind.SubtractAssignmentExpression))
                && assignment.Left == effective)
            {
                continue;
            }

            return "Methods that raise, invoke, or read a field-like event are skipped; "
                + "C# only allows += / -= on an event outside its declaring type, so the "
                + "shim cannot compile this body. Use uloop compile.";
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
                // whole query (including the source expression) as a closure body for v1.
                bodies.Add(queryExpression);
            }
        }

        return bodies;
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
        MethodTransformDecision current)
    {
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
                out AccessorPlan feasibilityPlan,
                out string accessorRejectReason))
        {
            return MethodTransformDecision.Skip(
                AddedMethodSkipReasons.InaccessibleAccessNoRewrite
                + " Accessor rewrite unavailable: "
                + accessorRejectReason
                + " Run 'uloop compile'.");
        }

        bool usesDelegation = feasibilityPlan.Entries.Count > 0;
        return MethodTransformDecision.AddedMethod(usesDelegation);
    }

    internal static string EvaluateAddedMethodSkipReason(
        IMethodSymbol methodSymbol,
        MethodDeclarationSyntax methodDeclaration)
    {
        if (methodSymbol.IsAbstract || methodSymbol.IsVirtual || methodSymbol.IsOverride)
        {
            return AddedMethodSkipReasons.VirtualOrAbstract;
        }

        bool hasTypeParameters = methodDeclaration != null && methodDeclaration.TypeParameterList != null;
        if (methodSymbol.IsGenericMethod || hasTypeParameters)
        {
            return AddedMethodSkipReasons.Generic;
        }

        return null;
    }
}
