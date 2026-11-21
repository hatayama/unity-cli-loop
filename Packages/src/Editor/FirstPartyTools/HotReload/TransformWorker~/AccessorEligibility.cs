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

/// <summary>
/// What: decides whether an async/iterator/closure private-access skip can be rescued by
/// rewriting inaccessible member accesses into Harmony accessor delegates (conditions b/c).
/// </summary>
internal static class AccessorEligibility
{
    // addedMemberAccess is null when the body's added members are not rewritten directly (a
    // property accessor body); then every inaccessible member goes through the plan.
    // accessRoots limits which accesses are registered (null registers the whole body). The type
    // visibility and property increment checks still look at the whole body: a closure that
    // captures an outer local carries its type into the display class.
    // targetAssembly is the compiled patch target read with every member visible, or null when the
    // run could not read it; an accessor may name a non-public class only by finding it there.
    public static bool TryBuildPlan(
        SemanticModel semanticModel,
        IMethodSymbol methodSymbol,
        INamedTypeSymbol typeSymbol,
        SyntaxNode bodyNode,
        AddedMemberAccessLookup addedMemberAccess,
        IReadOnlyList<SyntaxNode> accessRoots,
        IAssemblySymbol targetAssembly,
        out AccessorPlan plan,
        out WorkerReason rejectReason)
    {
        plan = null;
        bool built = TryBuildEntries(
            semanticModel,
            methodSymbol,
            bodyNode,
            addedMemberAccess,
            accessRoots,
            targetAssembly,
            out AccessorPlan entries,
            out rejectReason);

        // Why only an empty plan may skip the visible host: a body reaching only added members needs
        // no binder or delegate, so nothing has to name the host. An entry naming the host cannot be
        // written by the shim, and one naming another type has never been exercised on such a host.
        // Every other refusal keeps naming the host, because fixing that one alone rarely applies.
        if (!AccessibilityRules.IsExternallyVisibleType(typeSymbol)
            && (!built || entries.Entries.Count > 0))
        {
            rejectReason = WorkerReason.Of(HotReloadWorkerReasonCode.AccessorContainingTypeNotVisible);
            return false;
        }

        plan = entries;
        return built;
    }

    private static bool TryBuildEntries(
        SemanticModel semanticModel,
        IMethodSymbol methodSymbol,
        SyntaxNode bodyNode,
        AddedMemberAccessLookup addedMemberAccess,
        IReadOnlyList<SyntaxNode> accessRoots,
        IAssemblySymbol targetAssembly,
        out AccessorPlan plan,
        out WorkerReason rejectReason)
    {
        plan = null;
        rejectReason = null;

        if (!AreMethodSignatureTypesVisible(methodSymbol, out rejectReason))
        {
            return false;
        }

        List<ClassNamedByCall> classesNamedByCalls = new List<ClassNamedByCall>();
        if (!AreBodyTypeUsagesVisible(semanticModel, bodyNode, classesNamedByCalls, out rejectReason))
        {
            return false;
        }

        AccessorPlan built = new AccessorPlan();
        HashSet<SyntaxNode> registeredCalls = new HashSet<SyntaxNode>();
        IReadOnlyList<SyntaxNode> roots = accessRoots ?? new[] { bodyNode };
        foreach (SyntaxNode root in roots)
        {
            if (!TryRegisterAccesses(semanticModel, root, built, addedMemberAccess, registeredCalls, out rejectReason))
            {
                return false;
            }
        }

        // Why after registration: a class named only by a static-form call stays out of the shim only
        // when the call is rewritten, by an accessor or by the added-method rewrite, which is what
        // registering it means. A call outside the roots, such as a statement beside the closures of
        // a transplanted body, is not registered, so it would keep calling the non-public class as
        // written, and it stays refused.
        foreach (ClassNamedByCall classNamedByCall in classesNamedByCalls)
        {
            if (!registeredCalls.Contains(classNamedByCall.Call))
            {
                rejectReason = DescribeBodyTypeNotVisible(classNamedByCall.Class);
                return false;
            }
        }

        foreach (AccessorEntry entry in built.Entries)
        {
            if (entry.TryGetVisibilityFailure(targetAssembly, out rejectReason))
            {
                return false;
            }
        }

        WorkerReason incrementReject = FindPropertyIncrementRejectReasonOrNull(semanticModel, bodyNode, addedMemberAccess);
        if (incrementReject != null)
        {
            rejectReason = incrementReject;
            return false;
        }

        plan = built;
        rejectReason = null;
        return true;
    }

    private static bool TryRegisterAccesses(
        SemanticModel semanticModel,
        SyntaxNode root,
        AccessorPlan built,
        AddedMemberAccessLookup addedMemberAccess,
        HashSet<SyntaxNode> registeredCalls,
        out WorkerReason rejectReason)
    {
        foreach (SyntaxNode node in root.DescendantNodesAndSelf())
        {
            if (NameofRules.IsInsideNameofArgument(node))
            {
                continue;
            }

            if (AccessorAccessRegistrar.TryRegisterInaccessibleAccess(
                    semanticModel,
                    node,
                    built,
                    addedMemberAccess,
                    out rejectReason))
            {
                if (node is InvocationExpressionSyntax)
                {
                    registeredCalls.Add(node);
                }
            }
            else if (rejectReason != null)
            {
                return false;
            }
            else if (node is InvocationExpressionSyntax invocation
                && CallsAnAddedMethodOfTheEditedType(semanticModel, invocation, addedMemberAccess))
            {
                registeredCalls.Add(node);
            }
        }

        rejectReason = null;
        return true;
    }

    // Why a call the registrar declines can count as rewritten: a call to a method this reload adds
    // to the edited type takes no accessor because the added-method rewrite replaces it with a call
    // to the shim, so the class name it is written with does not reach the shim either.
    private static bool CallsAnAddedMethodOfTheEditedType(
        SemanticModel semanticModel,
        InvocationExpressionSyntax invocation,
        AddedMemberAccessLookup addedMemberAccess)
    {
        return addedMemberAccess != null
            && AccessibilityRules.ResolvedSymbolOrNull(semanticModel, invocation) is IMethodSymbol method
            && addedMemberAccess.IsAddedMethod(AccessibilityRules.UnreducedMethod(method));
    }

    private static bool AreMethodSignatureTypesVisible(IMethodSymbol methodSymbol, out WorkerReason rejectReason)
    {
        if (TryDescribeUnresolvedType(
                methodSymbol.ReturnType,
                HotReloadWorkerReasonCode.AccessorMethodReturnTypeUnresolved,
                out rejectReason))
        {
            return false;
        }

        if (!AccessibilityRules.IsExternallyVisibleType(methodSymbol.ReturnType))
        {
            rejectReason = WorkerReason.Of(HotReloadWorkerReasonCode.AccessorMethodReturnTypeNotVisible);
            return false;
        }

        foreach (IParameterSymbol parameter in methodSymbol.Parameters)
        {
            if (TryDescribeUnresolvedType(
                    parameter.Type,
                    HotReloadWorkerReasonCode.AccessorMethodParameterTypeUnresolved,
                    out rejectReason))
            {
                return false;
            }

            if (!AccessibilityRules.IsExternallyVisibleType(parameter.Type))
            {
                rejectReason =
                    WorkerReason.Of(HotReloadWorkerReasonCode.AccessorMethodParameterTypeNotVisible);
                return false;
            }
        }

        rejectReason = null;
        return true;
    }

    // Why before visibility: TypeKind.Error is not "invisible"; treating it as condition c
    // tells the caller the type exists but cannot be seen, which hides missing usings/typos.
    // Why recurse: List<MissingType>, MissingType[], and MissingType* have a resolved outer
    // kind, so only the type argument, element, or pointed-at type is Error.
    private static bool TryDescribeUnresolvedType(
        ITypeSymbol typeSymbol,
        HotReloadWorkerReasonCode code,
        out WorkerReason reason)
    {
        if (typeSymbol == null)
        {
            reason = null;
            return false;
        }

        if (typeSymbol.TypeKind == TypeKind.Error)
        {
            reason = WorkerReason.Of(
                code,
                typeSymbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));
            return true;
        }

        if (typeSymbol is IPointerTypeSymbol pointerType)
        {
            return TryDescribeUnresolvedType(pointerType.PointedAtType, code, out reason);
        }

        if (typeSymbol is IArrayTypeSymbol arrayType)
        {
            return TryDescribeUnresolvedType(arrayType.ElementType, code, out reason);
        }

        INamedTypeSymbol namedType = typeSymbol as INamedTypeSymbol;
        if (namedType == null)
        {
            reason = null;
            return false;
        }

        foreach (ITypeSymbol typeArgument in namedType.TypeArguments)
        {
            if (TryDescribeUnresolvedType(typeArgument, code, out reason))
            {
                return true;
            }
        }

        reason = null;
        return false;
    }

    // classesNamedByCalls receives each non-public class the body names only as the class of a
    // static-form call; the caller refuses it unless that call is registered. Whether an accessor may
    // then name the class is AccessorEntry's decision.
    private static bool AreBodyTypeUsagesVisible(
        SemanticModel semanticModel,
        SyntaxNode bodyNode,
        List<ClassNamedByCall> classesNamedByCalls,
        out WorkerReason rejectReason)
    {
        SemanticModel enforcingModel = null;
        foreach (SyntaxNode node in bodyNode.DescendantNodesAndSelf())
        {
            if (NameofRules.IsInsideNameofArgument(node))
            {
                continue;
            }

            ITypeSymbol typeSymbol = FindUsedTypeOrNull(semanticModel, node);
            if (typeSymbol != null && AccessibilityRules.IsExternallyVisibleType(typeSymbol))
            {
                continue;
            }

            // Why bind again with accessibility enforced when the lenient binding sees an error or an
            // invisible type: a referenced assembly carries internal copies of BCL types (Memory<T>,
            // ReadOnlySpan<T>), so with accessibility ignored their names are ambiguous, and
            // 'Func<Memory<int>, int>' read as a delegate over an error type the shim cannot see.
            // The enforcing binding resolves the name as Unity's compiler does.
            enforcingModel = enforcingModel ?? AccessibilityRules.EnforcingModel(semanticModel);
            ITypeSymbol enforcedType = FindUsedTypeOrNull(enforcingModel, node);
            if (enforcedType != null && enforcedType.TypeKind != TypeKind.Error)
            {
                typeSymbol = enforcedType;
            }

            // Why a type neither binding resolves is let through: the edited source must compile
            // before the reload runs, and the compiler reports it.
            if (typeSymbol == null || typeSymbol.TypeKind == TypeKind.Error)
            {
                continue;
            }

            if (AccessibilityRules.IsExternallyVisibleType(typeSymbol))
            {
                continue;
            }

            InvocationExpressionSyntax call = StaticFormCall.FindCallNamingTheClassOrNull(semanticModel, node);
            if (call != null)
            {
                classesNamedByCalls.Add(new ClassNamedByCall(call, typeSymbol));
                continue;
            }

            rejectReason = DescribeBodyTypeNotVisible(typeSymbol);
            return false;
        }

        rejectReason = null;
        return true;
    }

    private static WorkerReason DescribeBodyTypeNotVisible(ITypeSymbol typeSymbol)
    {
        return WorkerReason.Of(
            HotReloadWorkerReasonCode.AccessorBodyTypeNotVisible,
            typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
    }

    // A non-public class a body names only as the class of a static-form call.
    private sealed class ClassNamedByCall
    {
        internal ClassNamedByCall(InvocationExpressionSyntax call, ITypeSymbol @class)
        {
            Call = call;
            Class = @class;
        }

        internal InvocationExpressionSyntax Call { get; }

        internal ITypeSymbol Class { get; }
    }

    // What: the type a body node names or creates, or null when the node names none.
    private static ITypeSymbol FindUsedTypeOrNull(SemanticModel semanticModel, SyntaxNode node)
    {
        if (node is TypeSyntax typeSyntax)
        {
            return semanticModel.GetTypeInfo(typeSyntax).Type
                ?? semanticModel.GetSymbolInfo(typeSyntax).Symbol as ITypeSymbol;
        }

        if (node is VariableDeclarationSyntax variableDeclaration && variableDeclaration.Type.IsVar)
        {
            return semanticModel.GetTypeInfo(variableDeclaration.Type).Type;
        }

        if (node is ImplicitObjectCreationExpressionSyntax implicitObjectCreation)
        {
            return semanticModel.GetTypeInfo(implicitObjectCreation).Type;
        }

        return null;
    }

    // What: the reason an increment or decrement of an inaccessible property in the body cannot be
    // rewritten, or null when there is none. Why the struct reason for a struct instance property:
    // the increment reason suggests '+= 1', which is refused as well for such a property.
    private static WorkerReason FindPropertyIncrementRejectReasonOrNull(
        SemanticModel semanticModel,
        SyntaxNode bodyNode,
        AddedMemberAccessLookup addedMemberAccess)
    {
        foreach (SyntaxNode node in bodyNode.DescendantNodes())
        {
            if (NameofRules.IsInsideNameofArgument(node))
            {
                continue;
            }

            ExpressionSyntax operand = FindIncrementOrDecrementOperand(node);
            if (operand == null)
            {
                continue;
            }

            // The increment reads through the getter and stores through the accessor a write
            // calls, which for a ref-returning property is that getter again, not a setter.
            if (semanticModel.GetSymbolInfo(operand).Symbol is IPropertySymbol propertySymbol
                && !IsLeftToAddedPropertyScan(propertySymbol, addedMemberAccess)
                && (AccessibilityRules.IsInaccessibleAccessor(propertySymbol.GetMethod)
                    || AccessibilityRules.IsInaccessibleAccessor(
                        AssignmentTargetRules.AccessorCalledByWrite(propertySymbol))))
            {
                return WorkerReason.Of(AccessibilityRules.NeedsStructInstanceReceiver(propertySymbol)
                    ? HotReloadWorkerReasonCode.AccessorStructMemberNoShape
                    : HotReloadWorkerReasonCode.AccessorPropertyIncrementNoShape);
            }
        }

        return null;
    }

    private static ExpressionSyntax FindIncrementOrDecrementOperand(SyntaxNode node)
    {
        if (node is PostfixUnaryExpressionSyntax postfix
            && (postfix.IsKind(SyntaxKind.PostIncrementExpression)
                || postfix.IsKind(SyntaxKind.PostDecrementExpression)))
        {
            return postfix.Operand;
        }

        if (node is PrefixUnaryExpressionSyntax prefix
            && (prefix.IsKind(SyntaxKind.PreIncrementExpression)
                || prefix.IsKind(SyntaxKind.PreDecrementExpression)))
        {
            return prefix.Operand;
        }

        return null;
    }

    // Why an added property is left to the added-property scan: its reason names the one
    // rewrite that works there ('X = X + 1'), while this one also suggests 'X += 1', which that
    // scan skips for an added property.
    private static bool IsLeftToAddedPropertyScan(
        IPropertySymbol propertySymbol,
        AddedMemberAccessLookup addedMemberAccess)
    {
        return addedMemberAccess != null && addedMemberAccess.IsAddedProperty(propertySymbol);
    }

    internal static bool IsSupportedCompoundAssignmentKind(SyntaxKind kind)
    {
        return kind == SyntaxKind.AddAssignmentExpression
            || kind == SyntaxKind.SubtractAssignmentExpression
            || kind == SyntaxKind.MultiplyAssignmentExpression
            || kind == SyntaxKind.DivideAssignmentExpression
            || kind == SyntaxKind.ModuloAssignmentExpression
            || kind == SyntaxKind.AndAssignmentExpression
            || kind == SyntaxKind.ExclusiveOrAssignmentExpression
            || kind == SyntaxKind.OrAssignmentExpression
            || kind == SyntaxKind.LeftShiftAssignmentExpression
            || kind == SyntaxKind.RightShiftAssignmentExpression
            || kind == SyntaxKind.UnsignedRightShiftAssignmentExpression;
    }

    /// <summary>
    /// What: whether an assignment left's receiver chain is free of re-evaluable members
    /// (properties/methods). Only this/locals/parameters/fields (and type/namespace qualifiers)
    /// are allowed — FieldRef re-reads the same storage, so field links are idempotent.
    /// </summary>
    internal static bool IsSideEffectFreeAssignmentReceiver(
        SemanticModel semanticModel,
        ExpressionSyntax left)
    {
        // Why look through parentheses around the left: `(a.P) += 1` evaluates `a` twice exactly as
        // `a.P += 1` does, so the parentheses must not pass it off as a bare member.
        ExpressionSyntax target = AssignmentTargetRules.Unparenthesized(left);
        ExpressionSyntax receiver = target is MemberAccessExpressionSyntax memberAccess
            ? memberAccess.Expression
            : null;
        if (receiver == null)
        {
            // Bare member — implicit this.
            return true;
        }

        ExpressionSyntax current = receiver;
        while (current is MemberAccessExpressionSyntax nested)
        {
            ISymbol linkSymbol = semanticModel.GetSymbolInfo(nested.Name).Symbol
                ?? semanticModel.GetSymbolInfo(nested).Symbol;
            if (!IsSideEffectFreeReceiverLink(linkSymbol))
            {
                return false;
            }

            current = nested.Expression;
        }

        if (current is ThisExpressionSyntax || current is BaseExpressionSyntax)
        {
            return true;
        }

        if (current is IdentifierNameSyntax)
        {
            ISymbol headSymbol = semanticModel.GetSymbolInfo(current).Symbol;
            return headSymbol is ILocalSymbol
                || headSymbol is IParameterSymbol
                || headSymbol is IFieldSymbol
                || headSymbol is ITypeSymbol
                || headSymbol is INamespaceSymbol;
        }

        return false;
    }

    private static bool IsSideEffectFreeReceiverLink(ISymbol linkSymbol)
    {
        // Fields re-read the same storage; type/namespace qualifiers are not evaluated.
        return linkSymbol is IFieldSymbol
            || linkSymbol is ITypeSymbol
            || linkSymbol is INamespaceSymbol;
    }

    public static bool IsNameHandledByParent(SimpleNameSyntax node)
    {
        if (node.Parent is MemberAccessExpressionSyntax memberAccess && memberAccess.Name == node)
        {
            return true;
        }

        if (node.Parent is QualifiedNameSyntax qualifiedName && qualifiedName.Right == node)
        {
            return true;
        }

        if (node.Parent is MemberBindingExpressionSyntax memberBinding && memberBinding.Name == node)
        {
            return true;
        }

        // Invocation targets are NOT handled here: method groups are skipped by the caller after
        // a symbol check; delegate-typed field invokes must reach the field-read path.
        return false;
    }
}
