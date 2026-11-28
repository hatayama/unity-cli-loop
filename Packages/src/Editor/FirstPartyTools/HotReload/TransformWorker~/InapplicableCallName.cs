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
/// receiver type the compiler reports CS1929 instead of CS1061; when it takes the receiver with other
/// parameter types, CS1503; with fewer parameters, CS1501; with more, CS7036. Only the last two are
/// located on the name: CS1929 marks the receiver and CS1503 the argument, so the name is found from
/// the node the error marks.
/// </remarks>
internal static class InapplicableCallName
{
    private static readonly HashSet<string> InapplicableCallDiagnosticIds =
        new HashSet<string>(StringComparer.Ordinal) { "CS1929", "CS1503", "CS1501", "CS7036" };

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
    /// <paramref name="bodyNode"/>, reports: the name after the receiver CS1929 marks, of the call
    /// whose argument CS1503 marks, or the called name CS1501 and CS7036 mark. Null when the marked
    /// node is not the receiver, an argument or the name of such a call, such as a call by bare name.
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
            SimpleNameSyntax name = NameForMarkedNodeOrNull(error.Id, node);
            if (name != null)
            {
                return name;
            }
        }

        return null;
    }

    private static SimpleNameSyntax NameForMarkedNodeOrNull(string diagnosticId, SyntaxNode node)
    {
        switch (diagnosticId)
        {
            case "CS1929":
                return NameAfterReceiverOrNull(node);
            case "CS1503":
                return NameOfCallWithArgumentOrNull(node);
            default:
                return CalledNameOrNull(node);
        }
    }

    // 'receiver.Name(...)' or 'receiver?.Name(...)', where 'node' is 'Name', or the '.Name' binding,
    // which the compiler marks for a call after '?.'.
    private static SimpleNameSyntax CalledNameOrNull(SyntaxNode node)
    {
        if (node is MemberBindingExpressionSyntax marked)
        {
            return marked.Parent is InvocationExpressionSyntax call && call.Expression == marked ? marked.Name : null;
        }

        if (node is not SimpleNameSyntax name)
        {
            return null;
        }

        switch (name.Parent)
        {
            case MemberAccessExpressionSyntax access when access.Name == name:
                return access.Parent is InvocationExpressionSyntax invocation && invocation.Expression == access ? name : null;
            case MemberBindingExpressionSyntax binding when binding.Name == name:
                return CalledNameOrNull(binding);
            default:
                return null;
        }
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

        return node.Parent is ConditionalAccessExpressionSyntax conditional && conditional.Expression == node
            ? CalledBindingNameOrNull(conditional.WhenNotNull)
            : null;
    }

    // The name of the call that '?.' starts, the leftmost member binding of 'whenNotNull', which may
    // be followed by more calls or member accesses ('?.Name(...).Other()'); null when that binding is
    // not called.
    private static SimpleNameSyntax CalledBindingNameOrNull(ExpressionSyntax whenNotNull)
    {
        ExpressionSyntax current = whenNotNull;
        while (true)
        {
            switch (current)
            {
                case MemberBindingExpressionSyntax binding:
                    return binding.Parent is InvocationExpressionSyntax call && call.Expression == binding
                        ? binding.Name
                        : null;
                case InvocationExpressionSyntax invocation:
                    current = invocation.Expression;
                    break;
                case MemberAccessExpressionSyntax access:
                    current = access.Expression;
                    break;
                case ElementAccessExpressionSyntax elementAccess:
                    current = elementAccess.Expression;
                    break;
                default:
                    return null;
            }
        }
    }

    // 'receiver.Name(..., node, ...)' or 'receiver?.Name(..., node, ...)', where 'node' is an argument
    // or the expression of one. Why the expression too: for a named argument 'add: 1' the compiler
    // marks only '1', whose span is not the argument's.
    private static SimpleNameSyntax NameOfCallWithArgumentOrNull(SyntaxNode node)
    {
        SyntaxNode argument = node.Parent is ArgumentSyntax parent && parent.Expression == node ? parent : node;
        if (argument is not ArgumentSyntax { Parent: ArgumentListSyntax { Parent: InvocationExpressionSyntax invocation } })
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
