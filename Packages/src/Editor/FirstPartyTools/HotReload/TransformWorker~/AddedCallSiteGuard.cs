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

internal static class AddedCallSiteGuard
{
    internal static void SkipBodiesThatCannotUseAddedMethods(
        List<TypeEmitState> typeEmitStates,
        SemanticModel semanticModel,
        AddedMethodCatalog addedMethodCatalog,
        AddedFieldCatalog addedFieldCatalog,
        List<WorkerSkipped> skipped)
    {
        bool progressed;
        do
        {
            progressed = false;
            foreach (TypeEmitState typeState in typeEmitStates)
            {
                List<QueuedShimMethod> remaining = new List<QueuedShimMethod>();
                foreach (QueuedShimMethod queued in typeState.QueuedMethods)
                {
                    SyntaxNode bodyNode =
                        (SyntaxNode)queued.MethodDeclaration.Body ?? queued.MethodDeclaration.ExpressionBody;
                    string skipReason;
                    string calledAddedMethodKey;
                    (skipReason, calledAddedMethodKey) = EvaluateAddedCallSiteSkipReason(
                        bodyNode,
                        semanticModel,
                        addedMethodCatalog,
                        addedFieldCatalog);
                    if (skipReason != null)
                    {
                        skipped.Add(new WorkerSkipped
                        {
                            Method = TransformWorkerProgram.FormatMethodLabel(queued.MethodSymbol),
                            Reason = skipReason,
                            CalledAddedMethodKey = calledAddedMethodKey,
                            MethodKey = calledAddedMethodKey == null ? null : queued.MethodKey
                        });
                        if (queued.IsAddedMethod)
                        {
                            addedMethodCatalog.Unregister(queued.MethodKey);
                        }

                        progressed = true;
                        continue;
                    }

                    remaining.Add(queued);
                }

                typeState.QueuedMethods.Clear();
                typeState.QueuedMethods.AddRange(remaining);
            }
        }
        while (progressed);
    }

    internal static (string Reason, string CalledAddedMethodKey) EvaluateAddedCallSiteSkipReason(
        SyntaxNode bodyNode,
        SemanticModel semanticModel,
        AddedMethodCatalog addedMethodCatalog,
        AddedFieldCatalog addedFieldCatalog)
    {
        if (bodyNode == null)
        {
            return (null, null);
        }

        foreach (InvocationExpressionSyntax invocation in bodyNode.DescendantNodesAndSelf()
            .OfType<InvocationExpressionSyntax>())
        {
            if (NameofRules.IsNameofInvocation(invocation))
            {
                continue;
            }

            ISymbol symbol = semanticModel.GetSymbolInfo(invocation).Symbol;
            if (symbol is not IMethodSymbol methodSymbol)
            {
                continue;
            }

            string calledKey = TransformWorkerProgram.BuildMethodKeyFromSymbol(methodSymbol);
            // Why the receiver spine (not a WhenNotNull ancestor walk): other?.Inner.AddedPing()
            // and other?.Get().AddedPing() walk left to a MemberBinding. An ancestor walk also
            // matches argument-list / lambda invocations that are ordinary rewrite targets.
            if (IsConditionalAccessReceiverSpine(invocation)
                && addedMethodCatalog.IsClassifiedAdded(calledKey))
            {
                return (AddedMethodSkipReasons.ConditionalAccess, null);
            }

            if (addedMethodCatalog.IsUnavailableAdded(calledKey))
            {
                return (AddedMethodSkipReasons.UnavailableAddedCall, calledKey);
            }
        }

        if (BodyReferencesAddedMethodGroup(bodyNode, semanticModel, addedMethodCatalog))
        {
            return (AddedMethodSkipReasons.MethodGroupReference, null);
        }

        return (AddedFieldClassifier.EvaluateAddedFieldSkipReason(bodyNode, semanticModel, addedFieldCatalog), null);
    }

    internal static bool BodyReferencesAddedMethodGroup(
        SyntaxNode bodyNode,
        SemanticModel semanticModel,
        AddedMethodCatalog addedMethodCatalog)
    {
        foreach (IdentifierNameSyntax name in bodyNode.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>())
        {
            if (IsInvocationCalleeName(name) || NameofRules.IsInsideNameofArgument(name))
            {
                continue;
            }

            ISymbol symbol = semanticModel.GetSymbolInfo(name).Symbol;
            if (symbol is IMethodSymbol methodSymbol
                && addedMethodCatalog.IsClassifiedAdded(TransformWorkerProgram.BuildMethodKeyFromSymbol(methodSymbol)))
            {
                return true;
            }
        }

        foreach (MemberAccessExpressionSyntax access in bodyNode.DescendantNodesAndSelf()
            .OfType<MemberAccessExpressionSyntax>())
        {
            if ((access.Parent is InvocationExpressionSyntax invocation && invocation.Expression == access)
                || NameofRules.IsInsideNameofArgument(access))
            {
                continue;
            }

            ISymbol symbol = semanticModel.GetSymbolInfo(access).Symbol;
            if (symbol is IMethodSymbol methodSymbol
                && addedMethodCatalog.IsClassifiedAdded(TransformWorkerProgram.BuildMethodKeyFromSymbol(methodSymbol)))
            {
                return true;
            }
        }

        return false;
    }

    internal static bool IsInvocationCalleeName(IdentifierNameSyntax name)
    {
        if (name.Parent is InvocationExpressionSyntax invocation && invocation.Expression == name)
        {
            return true;
        }

        if (name.Parent is MemberAccessExpressionSyntax memberAccess
            && memberAccess.Name == name
            && memberAccess.Parent is InvocationExpressionSyntax memberInvocation
            && memberInvocation.Expression == memberAccess)
        {
            return true;
        }

        if (name.Parent is MemberBindingExpressionSyntax memberBinding
            && memberBinding.Name == name
            && memberBinding.Parent is InvocationExpressionSyntax bindingInvocation
            && bindingInvocation.Expression == memberBinding)
        {
            return true;
        }

        return false;
    }

    // Why unknown→false: MemberBinding/ElementBinding can appear as the leftmost receiver
    // only along MemberAccess / ElementAccess / Invocation / postfix ! / ConditionalAccess /
    // Parenthesized. Cast / new / await / ternary / literals are complete expressions;
    // ExtractReceiver splices them as valid source. Returning true here would skip ordinary
    // calls with a "conditional access" reason and would suppress accessor rewrite of private
    // methods on those receivers (fields would still rewrite — VisitMemberAccess has no guard).
    internal static bool IsConditionalAccessReceiverSpine(InvocationExpressionSyntax invocation)
    {
        ExpressionSyntax current = invocation.Expression;
        while (current != null)
        {
            if (current is MemberBindingExpressionSyntax || current is ElementBindingExpressionSyntax)
            {
                return true;
            }

            ExpressionSyntax unwrapped = TryUnwrapReceiverSpineExpression(current);
            if (unwrapped != null)
            {
                current = unwrapped;
                continue;
            }

            return false;
        }

        return false;
    }

    internal static ExpressionSyntax TryUnwrapReceiverSpineExpression(ExpressionSyntax expression)
    {
        if (expression is MemberAccessExpressionSyntax memberAccess)
        {
            return memberAccess.Expression;
        }

        if (expression is ElementAccessExpressionSyntax elementAccess)
        {
            return elementAccess.Expression;
        }

        if (expression is InvocationExpressionSyntax innerInvocation)
        {
            return innerInvocation.Expression;
        }

        if (expression is PostfixUnaryExpressionSyntax postfix
            && postfix.IsKind(SyntaxKind.SuppressNullableWarningExpression))
        {
            return postfix.Operand;
        }

        if (expression is ConditionalAccessExpressionSyntax conditionalAccess)
        {
            return conditionalAccess.Expression;
        }

        if (expression is ParenthesizedExpressionSyntax parenthesized)
        {
            return parenthesized.Expression;
        }

        return null;
    }

    internal static string[] CollectCalledAddedMethodKeys(
        SyntaxNode bodyNode,
        SemanticModel semanticModel,
        AddedMethodCatalog addedMethodCatalog,
        string selfMethodKey)
    {
        if (bodyNode == null)
        {
            return Array.Empty<string>();
        }

        HashSet<string> keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (InvocationExpressionSyntax invocation in bodyNode.DescendantNodesAndSelf()
            .OfType<InvocationExpressionSyntax>())
        {
            if (NameofRules.IsNameofInvocation(invocation))
            {
                continue;
            }

            ISymbol symbol = semanticModel.GetSymbolInfo(invocation).Symbol;
            if (symbol is not IMethodSymbol methodSymbol)
            {
                continue;
            }

            string calledKey = TransformWorkerProgram.BuildMethodKeyFromSymbol(methodSymbol);
            if (calledKey == selfMethodKey)
            {
                continue;
            }

            if (addedMethodCatalog.Contains(calledKey))
            {
                keys.Add(calledKey);
            }
        }

        if (keys.Count == 0)
        {
            return Array.Empty<string>();
        }

        string[] result = new string[keys.Count];
        keys.CopyTo(result);
        return result;
    }
}
