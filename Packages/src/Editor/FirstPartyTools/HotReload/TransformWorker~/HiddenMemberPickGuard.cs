using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

/// <summary>
/// What: refuses an edited body that names a member the patch could resolve differently from Unity's
/// compiler. The patch is compiled against a copy of the assembly where every member is public, so a
/// private or protected member that the code at the use cannot reach takes part in member lookup and
/// overload resolution there: an overload that fits the arguments better, or a member that hides the
/// one the code uses, is then called instead, in place or through a closure that throws for it.
/// Members declared in the edited source are compared through the two bindings; a type of the
/// assembly whose source the reload was not given, which the worker sees through its public surface
/// only, is compared by binding the use the way the patch's compile does (PatchBindingPickComparison).
/// </summary>
internal static class HiddenMemberPickGuard
{
    /// <summary>
    /// What: the skip reason for the first use in the body that the patch could resolve to a member
    /// the code there cannot reach, naming that member, or null when every use resolves alike.
    /// </summary>
    internal static WorkerReason DescribeSkipOrNull(
        SemanticModel semanticModel,
        SyntaxNode bodyNode,
        IAssemblySymbol targetAssembly,
        PatchBindingCompilation patchBinding)
    {
        SemanticModel enforcingModel = null;
        SemanticModel patchModel = null;
        foreach (SyntaxNode node in bodyNode.DescendantNodesAndSelf())
        {
            if (node is not ExpressionSyntax || NameofRules.IsInsideNameofArgument(node))
            {
                continue;
            }

            // Why a use the worker binds to nothing still goes to the patch's binding: an internal
            // member of a compiled type is not imported here, so a call whose only reachable
            // overload is internal binds to nothing, while the patch sees every overload.
            SymbolInfo info = semanticModel.GetSymbolInfo(node);
            bool boundHere = info.Symbol != null || !info.CandidateSymbols.IsEmpty;
            enforcingModel = enforcingModel ?? AccessibilityRules.EnforcingModel(semanticModel);
            patchModel = patchModel ?? patchBinding.ModelFor(semanticModel.SyntaxTree);
            ISymbol hidden = (boundHere ? FindHiddenPickOrNull(semanticModel, enforcingModel, node, info) : null)
                ?? PatchBindingPickComparison.FindHiddenPickOrNull(enforcingModel, patchModel, node, targetAssembly);
            if (hidden != null)
            {
                return WorkerReason.Of(HotReloadWorkerReasonCode.MethodTransformUseResolvesToHiddenMember, Display(hidden));
            }
        }

        return null;
    }

    // Why two cases: when the binding that ignores accessibility resolves a member, the patch's
    // compile resolves the same one, and Unity's compiler cannot pick a member the code cannot reach,
    // so the two differ even when Unity's pick is unknown here. When it resolves nothing (a referenced
    // assembly also declares Memory<T> internally, or both copies of a static class offer the method),
    // the patch's pick is unknown, and any candidate the code cannot reach that the arguments may fit,
    // other than Unity's pick, could be it, so the body fails closed.
    // Why Unity's pick is still asked for: a member found unreachable that Unity's compiler resolves too
    // is reachable after all, and comparing keeps such a body patched.
    private static ISymbol FindHiddenPickOrNull(
        SemanticModel semanticModel,
        SemanticModel enforcingModel,
        SyntaxNode node,
        SymbolInfo info)
    {
        if (info.Symbol != null)
        {
            if (!IsHiddenAt(semanticModel, node, info.Symbol))
            {
                return null;
            }

            ISymbol resolved = UnityPickOrNull(enforcingModel, node);
            return resolved != null && IsSameMember(info.Symbol, resolved) ? null : info.Symbol;
        }

        ISymbol unityPick = null;
        bool unityPickAsked = false;
        foreach (ISymbol candidate in info.CandidateSymbols)
        {
            if (!IsHiddenAt(semanticModel, node, candidate)
                || !HiddenCandidateApplicability.MayApply(enforcingModel, node, candidate))
            {
                continue;
            }

            if (!unityPickAsked)
            {
                unityPick = UnityPickOrNull(enforcingModel, node);
                unityPickAsked = true;
            }

            if (unityPick == null || !IsSameMember(candidate, unityPick))
            {
                return candidate;
            }
        }

        return null;
    }

    // Why null when the binding fails, rather than a guess among its candidates: which member Unity's
    // compiler picks is then not known here (an extension method of a public static class comes from
    // both the edited source and the compiled copy, and the call fails between the two), and the caller
    // refuses a body whose pick it cannot compare.
    private static ISymbol UnityPickOrNull(SemanticModel enforcingModel, SyntaxNode node)
    {
        return enforcingModel.GetSymbolInfo(node).Symbol;
    }

    // Why only members of the edited source: this compilation is another assembly than the compiled one
    // it references, so a member of the compiled assembly that code in it may reach (internal, private
    // protected) looks unreachable here, and the reachability of such a member cannot be told.
    // Why the declared accessibility first: a member every type of the assembly may reach, a local or a
    // parameter resolves alike in both compiles, and the accessibility check below asks for a binder.
    // Why the receiver's type: a protected instance member is reachable only through the code's own
    // type or a type derived from it, so a derived type cannot call it on a value typed as the base.
    private static bool IsHiddenAt(SemanticModel semanticModel, SyntaxNode node, ISymbol symbol)
    {
        if (!SymbolEqualityComparer.Default.Equals(symbol.ContainingAssembly, semanticModel.Compilation.Assembly)
            || IsReachableFromTheWholeAssembly(symbol))
        {
            return false;
        }

        ISymbol enclosing = semanticModel.GetEnclosingSymbol(node.SpanStart);
        INamedTypeSymbol enclosingType = enclosing as INamedTypeSymbol ?? enclosing?.ContainingType;
        if (enclosingType == null)
        {
            return false;
        }

        ISymbol member = symbol is IMethodSymbol method ? AccessibilityRules.UnreducedMethod(method) : symbol;
        return !semanticModel.Compilation.IsSymbolAccessibleWithin(
            member,
            enclosingType,
            FindReceiverTypeOrNull(semanticModel, node));
    }

    // Why base needs no exception, though C# lets a derived type reach a protected member through it
    // and this query is not told so: Unity's compiler resolves the same member there, and the caller
    // keeps a body whose member both bindings resolve alike.
    // Why the created type for a construction: C# lets a derived type call a protected constructor of
    // its base only from a constructor initializer, so 'new Base(...)' there reaches it through Base.
    // The syntax gives that type even when the call resolves to nothing.
    /// <summary>
    /// What: the type a use is made on: the receiver of a call or member access, the created type
    /// of a construction, or null for a use written without a receiver or a static one.
    /// </summary>
    internal static ITypeSymbol FindReceiverTypeOrNull(SemanticModel semanticModel, SyntaxNode node)
    {
        if (node is BaseObjectCreationExpressionSyntax)
        {
            ITypeSymbol createdType = semanticModel.GetTypeInfo(node).Type;
            return createdType == null || createdType.TypeKind == TypeKind.Error ? null : createdType;
        }

        IOperation operation = semanticModel.GetOperation(node);
        if (operation is IInvocationOperation invocation)
        {
            return invocation.Instance?.Type;
        }

        if (operation is IMemberReferenceOperation reference)
        {
            return reference.Instance?.Type;
        }

        return UnresolvedReceiverTypeOrNull(semanticModel, node);
    }

    // Why from the syntax: a call the binding resolves to nothing has no operation that carries its
    // receiver.
    private static ITypeSymbol UnresolvedReceiverTypeOrNull(SemanticModel semanticModel, SyntaxNode node)
    {
        ExpressionSyntax receiver = ReceiverExpressionOrNull(node);
        if (receiver == null)
        {
            return null;
        }

        ITypeSymbol receiverType = semanticModel.GetTypeInfo(receiver).Type;
        return receiverType == null || receiverType.TypeKind == TypeKind.Error ? null : receiverType;
    }

    /// <summary>
    /// What: the expression a member access, element access or conditional binding is made on, or
    /// null for a use written without a receiver.
    /// </summary>
    internal static ExpressionSyntax ReceiverExpressionOrNull(SyntaxNode node)
    {
        SyntaxNode callee = node is InvocationExpressionSyntax invocation ? invocation.Expression : node;
        if (callee is SimpleNameSyntax name
            && ((name.Parent is MemberAccessExpressionSyntax nameAccess && nameAccess.Name == name)
                || (name.Parent is MemberBindingExpressionSyntax nameBinding && nameBinding.Name == name)))
        {
            callee = name.Parent;
        }

        switch (callee)
        {
            case MemberAccessExpressionSyntax access:
                return access.Expression;
            case ElementAccessExpressionSyntax elementAccess:
                return elementAccess.Expression;
            case MemberBindingExpressionSyntax:
            case ElementBindingExpressionSyntax:
                return ConditionalReceiverOrNull(callee);
            default:
                return null;
        }
    }

    // Why the nearest conditional access whose tail holds the binding: in 'a?.b?.c' the '.c' binds
    // to the value of '.b', and '.b', which sits in the inner access's head, binds to 'a'.
    private static ExpressionSyntax ConditionalReceiverOrNull(SyntaxNode binding)
    {
        for (SyntaxNode current = binding.Parent; current != null; current = current.Parent)
        {
            if (current is ConditionalAccessExpressionSyntax conditional
                && conditional.WhenNotNull.Span.Contains(binding.Span))
            {
                return conditional.Expression;
            }
        }

        return null;
    }

    /// <summary>
    /// What: whether the member and every type that contains it are public, internal or protected
    /// internal, so every type of the declaring assembly may reach it.
    /// </summary>
    internal static bool IsReachableFromTheWholeAssembly(ISymbol symbol)
    {
        for (ISymbol current = symbol; current != null && current.Kind != SymbolKind.Namespace; current = current.ContainingType)
        {
            Accessibility accessibility = current.DeclaredAccessibility;
            if (accessibility != Accessibility.NotApplicable
                && accessibility != Accessibility.Public
                && accessibility != Accessibility.Internal
                && accessibility != Accessibility.ProtectedOrInternal)
            {
                return false;
            }
        }

        return true;
    }

    // Why the unreduced original definition: the two bindings may report one extension method in
    // its reduced and unreduced forms, or one generic member constructed apart.
    private static bool IsSameMember(ISymbol first, ISymbol second)
    {
        return SymbolEqualityComparer.Default.Equals(Definition(first), Definition(second));
    }

    private static ISymbol Definition(ISymbol symbol)
    {
        return symbol is IMethodSymbol method
            ? AccessibilityRules.UnreducedMethod(method).OriginalDefinition
            : symbol.OriginalDefinition;
    }

    // Why unreduced: overloads of an extension method differ in their parameter lists, and the reduced
    // form drops the receiver and the class that declares them.
    private static string Display(ISymbol symbol)
    {
        ISymbol shown = symbol is IMethodSymbol method ? AccessibilityRules.UnreducedMethod(method) : symbol;
        return shown.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat);
    }
}
