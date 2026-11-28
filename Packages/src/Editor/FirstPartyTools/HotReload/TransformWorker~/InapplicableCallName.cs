using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

/// <summary>
/// What: the called name of a call 'receiver.Name(...)' or 'receiver?.Name(...)' that the worker's
/// binding rejected because every method of that name it can see fails to take the call, while an
/// internal extension method declared in a file the run was not given may be the one the call means.
/// </summary>
/// <remarks>
/// Why these errors: the worker's binding imports the target assembly through its public surface
/// only, so a same-named public extension method is all it can see. When that method takes another
/// receiver type the compiler reports CS1929 instead of CS1061, and when it takes the receiver with
/// other parameter types it reports CS1503. Neither error is located on the name: CS1929 marks the
/// receiver and CS1503 the argument, so the name is found from the node the error marks.
/// </remarks>
internal static class InapplicableCallName
{
    private static readonly HashSet<string> InapplicableCallDiagnosticIds =
        new HashSet<string>(StringComparer.Ordinal) { "CS1929", "CS1503" };

    /// <summary>
    /// What: whether <paramref name="error"/> reports a call whose name only binds to methods that
    /// do not take it.
    /// </summary>
    internal static bool IsInapplicableCallDiagnostic(Diagnostic error)
    {
        return InapplicableCallDiagnosticIds.Contains(error.Id);
    }

    /// <summary>
    /// What: the called name of the call <paramref name="error"/>, an inapplicable-call error inside
    /// <paramref name="bodyNode"/>, reports: the name after the receiver CS1929 marks, or of the call
    /// whose argument CS1503 marks. Null when the marked node is not the receiver or an argument of
    /// such a call, such as a call by bare name.
    /// </summary>
    internal static SimpleNameSyntax FindOrNull(Diagnostic error, SyntaxNode bodyNode)
    {
        Location location = error.Location;
        if (!location.IsInSource
            || location.SourceTree != bodyNode.SyntaxTree
            || !bodyNode.Span.Contains(location.SourceSpan))
        {
            return null;
        }

        // Why every node with the marked span and not the innermost one: a receiver such as
        // 'this._seed' shares its span with no inner node, while a literal argument shares it with
        // the ArgumentSyntax around it, and only the right one of those has the call as its parent.
        for (SyntaxNode node = bodyNode.FindNode(location.SourceSpan, getInnermostNodeForTie: true);
            node != null && node.Span == location.SourceSpan;
            node = node.Parent)
        {
            SimpleNameSyntax name = error.Id == "CS1929" ? NameAfterReceiverOrNull(node) : NameOfCallWithArgumentOrNull(node);
            if (name != null)
            {
                return name;
            }
        }

        return null;
    }

    // 'receiver.Name(...)' or 'receiver?.Name(...)', where 'node' is the receiver.
    // Why through parentheses: the compiler marks a parenthesized receiver without them, so
    // '((int?)50)?.Name(...)' marks '(int?)50'.
    private static SimpleNameSyntax NameAfterReceiverOrNull(SyntaxNode marked)
    {
        SyntaxNode node = marked;
        while (node.Parent is ParenthesizedExpressionSyntax parenthesized)
        {
            node = parenthesized;
        }

        if (node.Parent is MemberAccessExpressionSyntax access
            && access.Expression == node
            && access.Parent is InvocationExpressionSyntax invocation
            && invocation.Expression == access)
        {
            return access.Name;
        }

        return node.Parent is ConditionalAccessExpressionSyntax conditional
            && conditional.Expression == node
            && conditional.WhenNotNull is InvocationExpressionSyntax { Expression: MemberBindingExpressionSyntax binding }
                ? binding.Name
                : null;
    }

    // 'receiver.Name(..., node, ...)' or 'receiver?.Name(..., node, ...)', where 'node' is an argument.
    private static SimpleNameSyntax NameOfCallWithArgumentOrNull(SyntaxNode node)
    {
        if (node is not ArgumentSyntax { Parent: ArgumentListSyntax { Parent: InvocationExpressionSyntax invocation } })
        {
            return null;
        }

        switch (invocation.Expression)
        {
            case MemberAccessExpressionSyntax access:
                return access.Name;
            case MemberBindingExpressionSyntax binding:
                return binding.Name;
            default:
                return null;
        }
    }
}
