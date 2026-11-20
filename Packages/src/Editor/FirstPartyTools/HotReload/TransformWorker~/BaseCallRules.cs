using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using io.github.hatayama.UnityCliLoop.FirstPartyTools;

/// <summary>
/// Decides whether a body's uses of <c>base</c> let hot reload patch it. Only a direct
/// <c>base.M(args)</c> call in a compiled method's own statements can be redirected; every other
/// use yields the reason the body stays Skipped.
/// </summary>
internal static class BaseCallRules
{
    /// <summary>
    /// Why the body cannot be patched because of how it uses <c>base</c>; null when it does not
    /// use <c>base</c> or every use is a base method call hot reload can redirect.
    /// </summary>
    internal static WorkerReason EvaluateSkipReason(
        MethodDeclarationSyntax methodDeclaration,
        INamedTypeSymbol typeSymbol,
        SyntaxNode bodyNode,
        SemanticModel semanticModel,
        INamedTypeSymbol compiledType)
    {
        List<BaseExpressionSyntax> baseExpressions = bodyNode.DescendantNodes().OfType<BaseExpressionSyntax>().ToList();
        if (baseExpressions.Count == 0)
        {
            return null;
        }

        // Why the non-call check runs first: a base property or method group cannot be fixed by
        // moving it, so that guidance must win over the placement guidance below.
        List<BaseCall> calls = CollectDirectBaseCallsOrNull(baseExpressions, semanticModel);
        if (calls == null)
        {
            return WorkerReason.Of(HotReloadWorkerReasonCode.MethodTransformBaseMemberCall);
        }

        return FindPlacementReason(methodDeclaration, bodyNode, compiledType, baseExpressions)
            ?? FindConditionalTargetReason(calls)
            ?? FindArgumentShapeReason(calls)
            ?? FindAmbiguousTargetReason(calls)
            ?? FindTargetReason(typeSymbol, compiledType, calls);
    }

    /// <summary>
    /// Why a member or type hot reload added cannot host a base call; null when the host is a
    /// compiled method.
    /// </summary>
    internal static WorkerReason EvaluateHostSkipReason(
        bool isAddedMethod,
        bool servedByRetainedArtifact,
        SyntaxNode bodyNode,
        SemanticModel semanticModel)
    {
        if (bodyNode == null || !MethodTransformDecider.ContainsBaseExpression(bodyNode))
        {
            return null;
        }

        if (!isAddedMethod && !servedByRetainedArtifact)
        {
            return null;
        }

        // Same precedence as EvaluateSkipReason: a use no placement can fix is named first.
        List<BaseExpressionSyntax> baseExpressions = bodyNode.DescendantNodes().OfType<BaseExpressionSyntax>().ToList();
        if (CollectDirectBaseCallsOrNull(baseExpressions, semanticModel) == null)
        {
            return WorkerReason.Of(HotReloadWorkerReasonCode.MethodTransformBaseMemberCall);
        }

        return WorkerReason.Of(HotReloadWorkerReasonCode.MethodTransformBaseCallOutsideOwnStatements);
    }

    /// <summary>
    /// The <c>base.M(args)</c> invocation this base expression is the receiver of; null for any
    /// other use (base.Value, base[i], the method group base.M).
    /// </summary>
    internal static InvocationExpressionSyntax TryGetDirectBaseInvocation(BaseExpressionSyntax baseExpression)
    {
        if (baseExpression.Parent is not MemberAccessExpressionSyntax memberAccess
            || !memberAccess.IsKind(SyntaxKind.SimpleMemberAccessExpression)
            || memberAccess.Expression != baseExpression)
        {
            return null;
        }

        if (memberAccess.Parent is InvocationExpressionSyntax invocation && invocation.Expression == memberAccess)
        {
            return invocation;
        }

        return null;
    }

    /// <summary>
    /// How many BaseType hops separate <paramref name="typeSymbol"/> from the type that declares
    /// the base call's target.
    /// </summary>
    internal static int FindBaseDepth(INamedTypeSymbol typeSymbol, INamedTypeSymbol declaringType)
    {
        int depth = 0;
        for (INamedTypeSymbol ancestor = typeSymbol.BaseType; ancestor != null; ancestor = ancestor.BaseType)
        {
            depth++;
            if (SymbolEqualityComparer.Default.Equals(ancestor.OriginalDefinition, declaringType.OriginalDefinition))
            {
                return depth;
            }
        }

        throw new InvalidOperationException("A base call target is not declared on a base type of its caller.");
    }

    // Null when any base use is not a direct call to an ordinary instance method.
    private static List<BaseCall> CollectDirectBaseCallsOrNull(
        List<BaseExpressionSyntax> baseExpressions,
        SemanticModel semanticModel)
    {
        List<BaseCall> calls = new List<BaseCall>();
        foreach (BaseExpressionSyntax baseExpression in baseExpressions)
        {
            InvocationExpressionSyntax invocation = TryGetDirectBaseInvocation(baseExpression);
            if (invocation == null)
            {
                return null;
            }

            if (semanticModel.GetSymbolInfo(invocation).Symbol is not IMethodSymbol target
                || target.MethodKind != MethodKind.Ordinary
                || target.IsStatic)
            {
                return null;
            }

            calls.Add(new BaseCall(invocation, target));
        }

        return calls;
    }

    // Why these places: a base call is redirected only in IL the Editor transplants into the
    // compiled method, and closures, state machines, accessors and hosts without a compiled
    // method keep their IL elsewhere.
    private static WorkerReason FindPlacementReason(
        MethodDeclarationSyntax methodDeclaration,
        SyntaxNode bodyNode,
        INamedTypeSymbol compiledType,
        List<BaseExpressionSyntax> baseExpressions)
    {
        if (methodDeclaration == null
            || MethodTransformDecider.IsAsyncOrIterator(methodDeclaration, bodyNode)
            || compiledType == null
            || baseExpressions.Any(baseExpression => IsInsideNestedFunction(baseExpression, bodyNode)))
        {
            return WorkerReason.Of(HotReloadWorkerReasonCode.MethodTransformBaseCallOutsideOwnStatements);
        }

        return null;
    }

    // Why refused: the compiler drops a call to a [Conditional] method, and its argument
    // evaluation, when the symbol is undefined; the stand-in call would run both.
    private static WorkerReason FindConditionalTargetReason(List<BaseCall> calls)
    {
        foreach (BaseCall call in calls)
        {
            if (IsConditional(call.Target))
            {
                return WorkerReason.Of(HotReloadWorkerReasonCode.MethodTransformBaseCallConditionalTarget);
            }
        }

        return null;
    }

    // Why walk OverriddenMethod: an override of a [Conditional] method is conditional too, but the
    // attribute stays on the method it overrides.
    internal static bool IsConditional(IMethodSymbol target)
    {
        for (IMethodSymbol method = target; method != null; method = method.OverriddenMethod)
        {
            if (method.GetAttributes().Any(attribute =>
                attribute.AttributeClass?.ToDisplayString() == "System.Diagnostics.ConditionalAttribute"))
            {
                return true;
            }
        }

        return false;
    }

    // Why refused: the stand-in and the Editor identify the target by name and parameter types,
    // which cannot tell Pick(T) from Pick(int) on Base<int>, nor Take(out T) from Take(ref int).
    private static WorkerReason FindAmbiguousTargetReason(List<BaseCall> calls)
    {
        foreach (BaseCall call in calls)
        {
            if (HasCollidingOverload(call.Target))
            {
                return WorkerReason.Of(HotReloadWorkerReasonCode.MethodTransformBaseCallAmbiguousTarget);
            }
        }

        return null;
    }

    private static bool HasCollidingOverload(IMethodSymbol target)
    {
        string[] targetParameterTypes = ToByRefAgnosticParameterTypes(target);
        foreach (ISymbol member in target.ContainingType.GetMembers(target.Name))
        {
            if (member is not IMethodSymbol other
                || SymbolEqualityComparer.Default.Equals(other, target)
                || other.MethodKind != MethodKind.Ordinary
                || other.IsStatic
                || other.IsGenericMethod)
            {
                continue;
            }

            if (ToByRefAgnosticParameterTypes(other).SequenceEqual(targetParameterTypes))
            {
                return true;
            }
        }

        return false;
    }

    // Why the Cecil names: ref, out and in all map to the same "&" name, which is how the Editor
    // sees them after substitution.
    private static string[] ToByRefAgnosticParameterTypes(IMethodSymbol method)
    {
        return method.Parameters.Select(CecilTypeNames.ToParameterTypeFullName).ToArray();
    }

    private static WorkerReason FindArgumentShapeReason(List<BaseCall> calls)
    {
        foreach (BaseCall call in calls)
        {
            if (HasUnsupportedArgumentShape(call.Invocation, call.Target))
            {
                return WorkerReason.Of(HotReloadWorkerReasonCode.MethodTransformBaseCallArgumentShape);
            }
        }

        return null;
    }

    private static WorkerReason FindTargetReason(
        INamedTypeSymbol typeSymbol,
        INamedTypeSymbol compiledType,
        List<BaseCall> calls)
    {
        foreach (BaseCall call in calls)
        {
            if (!TargetIsCompiled(typeSymbol, compiledType, call.Target))
            {
                return WorkerReason.Of(HotReloadWorkerReasonCode.MethodTransformBaseCallTargetNotCompiled);
            }
        }

        return null;
    }

    private static bool IsInsideNestedFunction(SyntaxNode node, SyntaxNode bodyNode)
    {
        for (SyntaxNode current = node.Parent; current != null && current != bodyNode; current = current.Parent)
        {
            if (current is AnonymousFunctionExpressionSyntax
                || current is LocalFunctionStatementSyntax
                || current is QueryExpressionSyntax)
            {
                return true;
            }
        }

        return false;
    }

    // Why these shapes are refused: the stand-in copies the target's parameters without default
    // values or params, and a generic, ref-returning or pointer signature has no stand-in the
    // shim can declare and the Editor can match back.
    // Why the pointer checks stay although the unsafe-code skip runs first: the stand-in declares
    // the target's signature in a shim compiled without unsafe code, so it checks that signature
    // itself instead of relying on the member-level check running first.
    private static bool HasUnsupportedArgumentShape(InvocationExpressionSyntax invocation, IMethodSymbol target)
    {
        if (target.IsGenericMethod || target.ReturnsByRef || target.ReturnsByRefReadonly || UnsafeContextRules.IsPointerLike(target.ReturnType))
        {
            return true;
        }

        foreach (IParameterSymbol parameter in target.Parameters)
        {
            if (parameter.IsParams || !IsSupportedRefKind(parameter.RefKind) || UnsafeContextRules.IsPointerLike(parameter.Type))
            {
                return true;
            }
        }

        SeparatedSyntaxList<ArgumentSyntax> arguments = invocation.ArgumentList.Arguments;
        if (arguments.Any(argument => argument.NameColon != null))
        {
            return true;
        }

        // An omitted optional argument leaves fewer arguments than parameters.
        return arguments.Count != target.Parameters.Length;
    }

    private static bool IsSupportedRefKind(RefKind refKind)
    {
        return refKind == RefKind.None || refKind == RefKind.Ref || refKind == RefKind.Out || refKind == RefKind.In;
    }


    private static bool TargetIsCompiled(INamedTypeSymbol typeSymbol, INamedTypeSymbol compiledType, IMethodSymbol target)
    {
        int depth = FindBaseDepth(typeSymbol, target.ContainingType);
        INamedTypeSymbol compiledAncestor = compiledType;
        INamedTypeSymbol sourceAncestor = typeSymbol;
        for (int hop = 0; hop < depth; hop++)
        {
            compiledAncestor = compiledAncestor.BaseType;
            sourceAncestor = sourceAncestor.BaseType;
            if (compiledAncestor == null)
            {
                return false;
            }
        }

        // Why compare names: the base list may have changed since the last compile, so the same
        // depth can reach a different type in the compiled assembly.
        if (compiledAncestor.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            != sourceAncestor.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
        {
            return false;
        }

        IMethodSymbol compiledTarget = CompiledMemberMatcher.FindCompiledOrdinaryMethod(compiledAncestor, target);
        return compiledTarget != null
            && !compiledTarget.IsAbstract
            && CompiledMemberMatcher.ReturnTypesMatch(compiledTarget, target)
            && RefKindsMatch(compiledTarget, target);
    }

    // Why compare RefKind: ref, out and in all share one by-reference parameter type, so a base
    // target re-signatured between them since the compile would otherwise still match.
    private static bool RefKindsMatch(IMethodSymbol compiledTarget, IMethodSymbol target)
    {
        for (int index = 0; index < target.Parameters.Length; index++)
        {
            if (compiledTarget.Parameters[index].RefKind != target.Parameters[index].RefKind)
            {
                return false;
            }
        }

        return true;
    }

    private readonly struct BaseCall
    {
        internal BaseCall(InvocationExpressionSyntax invocation, IMethodSymbol target)
        {
            Invocation = invocation;
            Target = target;
        }

        internal InvocationExpressionSyntax Invocation { get; }

        internal IMethodSymbol Target { get; }
    }
}
