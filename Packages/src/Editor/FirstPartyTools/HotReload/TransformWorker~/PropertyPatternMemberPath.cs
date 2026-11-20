using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using io.github.hatayama.UnityCliLoop.FirstPartyTools;

/// <summary>
/// What: the member path a property pattern reads on the matched value: the name in '{ M: 1 }',
/// and every link of '{ A.B: 1 }'. Such a link is looked up on the matched value, so it takes no
/// receiver, and only a name can stand where it stands: the shim keeps it as written, and a body
/// whose link needs an accessor or an added-member read is refused instead.
/// </summary>
internal static class PropertyPatternMemberPath
{
    internal static bool Contains(ExpressionSyntax expression)
    {
        SyntaxNode path = WholePath(expression);
        // Why the colon's parent must be a subpattern: a NameColonSyntax also names an argument, as
        // in Call(value: x), and that name is not read from anything.
        return path.Parent is BaseExpressionColonSyntax colon
            && colon.Expression == path
            && colon.Parent is SubpatternSyntax;
    }

    /// <summary>
    /// The whole member path a link belongs to, as written: "A.B" for either link of '{ A.B: 1 }'.
    /// A skip reason shows it so the same read can be written outside the pattern.
    /// </summary>
    internal static string PathText(ExpressionSyntax link)
    {
        return WholePath(link).ToString();
    }

    // Why climb member accesses only: an extended pattern's path is a chain of them, and any other
    // parent (an argument, an operator) puts the node outside the path.
    private static SyntaxNode WholePath(ExpressionSyntax expression)
    {
        SyntaxNode current = expression;
        while (current.Parent is MemberAccessExpressionSyntax access)
        {
            current = access;
        }

        return current;
    }

    /// <summary>
    /// The accessor-plan refusal for a link naming a member the shim assembly cannot reach, or null
    /// when the shim can read it as written.
    /// </summary>
    internal static WorkerReason UnreachableLinkReasonOrNull(SemanticModel semanticModel, ExpressionSyntax link)
    {
        ISymbol member = semanticModel.GetSymbolInfo(link).Symbol;
        bool unreachable = member switch
        {
            // Why the getter: the pattern calls it, so a public property whose getter alone is
            // private is out of reach too. Why a missing getter is left alone: the worker sees a
            // file the reload was not given through its public surface, where an internal getter
            // reads as none, and the unpassed-member guard reports that use with its file.
            IPropertySymbol property => property.GetMethod != null
                && AccessibilityRules.IsInaccessibleAccessor(property.GetMethod),
            // Why any other member too: a field, or an event a pattern reads as one, has no shape
            // either; a positional pattern's parameter name is never inaccessible.
            _ => member != null && AccessibilityRules.IsInaccessibleFromExternalAssembly(member)
        };
        return unreachable
            ? WorkerReason.Of(HotReloadWorkerReasonCode.AccessorPropertyPatternNoShape, member.Name, PathText(link))
            : null;
    }

    /// <summary>
    /// Returns <paramref name="kept"/>, the link as written, when the rewriter found no replacement
    /// for it, and throws when it did.
    /// </summary>
    // Why throw instead of returning either node: the replacement cannot stand in a pattern and
    // fails the whole file, and keeping the name would compile a use the shim cannot make at run
    // time. The accessor plan and the added-member scans refuse every such body before emission,
    // so a replacement here means one of them missed a shape.
    internal static SyntaxNode RequireNoReplacement(SyntaxNode link, SyntaxNode replacement, SyntaxNode kept)
    {
        if (replacement != null)
        {
            throw new InvalidOperationException(
                "Property pattern member '" + link + "' reached the shim rewriter needing a rewrite; "
                + "the body must be refused before emission.");
        }

        return kept;
    }
}
