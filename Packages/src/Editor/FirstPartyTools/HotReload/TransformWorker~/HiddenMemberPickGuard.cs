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
/// Only members declared in the edited source are compared; the worker sees other types of the
/// assembly through their public surface only.
/// </summary>
internal static class HiddenMemberPickGuard
{
    /// <summary>
    /// What: the skip reason for the first use in the body that the patch could resolve to a member
    /// the code there cannot reach, naming that member, or null when every use resolves alike.
    /// </summary>
    internal static WorkerReason DescribeSkipOrNull(SemanticModel semanticModel, SyntaxNode bodyNode)
    {
        SemanticModel enforcingModel = null;
        foreach (SyntaxNode node in bodyNode.DescendantNodesAndSelf())
        {
            if (node is not ExpressionSyntax || NameofRules.IsInsideNameofArgument(node))
            {
                continue;
            }

            SymbolInfo info = semanticModel.GetSymbolInfo(node);
            if (info.Symbol == null && info.CandidateSymbols.IsEmpty)
            {
                continue;
            }

            enforcingModel = enforcingModel ?? AccessibilityRules.EnforcingModel(semanticModel);
            ISymbol hidden = FindHiddenPickOrNull(semanticModel, enforcingModel, node, info);
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

            ISymbol resolved = ResolvedByUnityOrNull(semanticModel, enforcingModel, node);
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
                unityPick = ResolvedByUnityOrNull(semanticModel, enforcingModel, node);
                unityPickAsked = true;
            }

            if (unityPick == null || !IsSameMember(candidate, unityPick))
            {
                return candidate;
            }
        }

        return null;
    }

    // Why the edited copy when the binding fails: the compiled assembly is referenced beside the edited
    // source, so an extension method of a public static class comes from both copies and a call to it
    // fails here between the two, while Unity's compile has only the edited one. Null when more than one
    // edited overload is left, since which of them Unity's compiler picks is not known here.
    private static ISymbol ResolvedByUnityOrNull(
        SemanticModel semanticModel,
        SemanticModel enforcingModel,
        SyntaxNode node)
    {
        SymbolInfo info = enforcingModel.GetSymbolInfo(node);
        if (info.Symbol != null
            || (info.CandidateReason != CandidateReason.Ambiguous
                && info.CandidateReason != CandidateReason.OverloadResolutionFailure))
        {
            return info.Symbol;
        }

        ISymbol editedCopy = null;
        foreach (ISymbol candidate in info.CandidateSymbols)
        {
            if (!SymbolEqualityComparer.Default.Equals(candidate.ContainingAssembly, semanticModel.Compilation.Assembly))
            {
                continue;
            }

            if (editedCopy != null)
            {
                return null;
            }

            editedCopy = candidate;
        }

        return editedCopy;
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
    private static ITypeSymbol FindReceiverTypeOrNull(SemanticModel semanticModel, SyntaxNode node)
    {
        IOperation operation = semanticModel.GetOperation(node);
        IOperation instance = operation is IInvocationOperation invocation
            ? invocation.Instance
            : (operation as IMemberReferenceOperation)?.Instance;
        return instance?.Type;
    }

    private static bool IsReachableFromTheWholeAssembly(ISymbol symbol)
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
