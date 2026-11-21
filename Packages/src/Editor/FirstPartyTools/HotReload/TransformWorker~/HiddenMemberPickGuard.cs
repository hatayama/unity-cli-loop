using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

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
    /// What: the skip reason for the first use in the body whose member the patch could resolve to a
    /// member the code there cannot reach, naming that member and the one Unity's compiler resolves,
    /// or null when every use resolves alike.
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
            ISymbol hidden = FindHiddenPickOrNull(semanticModel, enforcingModel, node, info, out ISymbol resolved);
            if (hidden != null)
            {
                return WorkerReason.Of(
                    HotReloadWorkerReasonCode.MethodTransformUseResolvesToHiddenMember,
                    Display(hidden),
                    Display(resolved));
            }
        }

        return null;
    }

    // Why two cases: when the binding that ignores accessibility resolves a member, the patch's
    // compile resolves the same one, so a member the code cannot reach is a different pick from
    // Unity's. When it resolves nothing (a referenced assembly also declares Memory<T> internally,
    // so the argument's type is ambiguous there), the patch's pick is unknown, and any candidate the
    // code cannot reach other than Unity's pick could be it, so the body fails closed.
    // Why nothing when Unity's binding resolves nothing: the edited source does not compile there,
    // which other checks report.
    private static ISymbol FindHiddenPickOrNull(
        SemanticModel semanticModel,
        SemanticModel enforcingModel,
        SyntaxNode node,
        SymbolInfo info,
        out ISymbol resolved)
    {
        resolved = null;
        if (info.Symbol != null)
        {
            if (!IsHiddenAt(enforcingModel, node, info.Symbol))
            {
                return null;
            }

            resolved = ResolvedByUnityOrNull(semanticModel, enforcingModel, node);
            return resolved != null && !IsSameMember(info.Symbol, resolved) ? info.Symbol : null;
        }

        foreach (ISymbol candidate in info.CandidateSymbols)
        {
            if (!IsHiddenAt(enforcingModel, node, candidate))
            {
                continue;
            }

            resolved = resolved ?? ResolvedByUnityOrNull(semanticModel, enforcingModel, node);
            if (resolved == null)
            {
                return null;
            }

            if (!IsSameMember(candidate, resolved))
            {
                return candidate;
            }
        }

        return null;
    }

    // Why the edited copy when the binding fails: the compiled assembly is referenced beside the edited
    // source, so an extension method of a public static class comes from both copies and a call to it
    // fails here between the two, while Unity's compile has only the edited one.
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

    // Why no check that the member is declared in the edited assembly, although only that assembly is
    // compiled with every member public: the binding imports no private or internal member of another
    // assembly, so such a member never reaches here.
    // Why the declared accessibility first: a member every type of the assembly may reach, a local or a
    // parameter resolves alike in both compiles, and asking the enforcing binding about it costs a
    // binder per use.
    private static bool IsHiddenAt(SemanticModel enforcingModel, SyntaxNode node, ISymbol symbol)
    {
        return !IsReachableFromTheWholeAssembly(symbol)
            && !enforcingModel.IsAccessible(node.SpanStart, symbol);
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
