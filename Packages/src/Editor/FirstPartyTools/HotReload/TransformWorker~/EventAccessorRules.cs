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

/// <summary>
/// What: decides how a body's field-like event uses are handled — rewritten through the event's
/// backing field, left on the publicized add/remove accessors, or skipped.
/// </summary>
internal static class EventAccessorRules
{
    internal const string CustomAccessorReason =
        "Methods that raise or read an event with custom add/remove accessors are skipped; "
        + "there is no backing field for the shim to reach. Use uloop compile.";

    internal const string NoBackingFieldReason =
        "Methods that raise or read an abstract, extern, or interface event are skipped; "
        + "there is no backing field for the shim to reach. Use uloop compile.";

    internal const string DelegateTypeNotVisibleReason =
        "Methods that raise or read an event whose delegate type is not visible from an external "
        + "assembly are skipped; the shim cannot name the accessor field type. Use uloop compile.";

    internal const string AddedInThisEditReason =
        "Methods that raise a field-like event added in this edit are skipped; "
        + "the compiled assembly has no backing field yet. Use uloop compile.";

    internal const string NameofReason =
        "Methods that name a field-like event inside nameof are skipped; the shim is a different "
        + "type and cannot keep the bare event name. Use uloop compile.";

    internal const string AccessorRewriteUnavailableReasonPrefix =
        "Methods that raise or read a field-like event are skipped when the body cannot be "
        + "rewritten into accessor delegates. Accessor rewrite unavailable: ";

    /// <summary>
    /// What: the skip reason for a body's event uses, or null when every use is either a
    /// subscription (+= / -=) or rewritable through the backing field.
    /// </summary>
    internal static string EvaluateEventUseSkipReason(
        SyntaxNode bodyNode,
        SemanticModel semanticModel,
        INamedTypeSymbol compiledType)
    {
        foreach (EventUse use in EnumerateEventUses(bodyNode, semanticModel))
        {
            if (use.IsSubscription)
            {
                continue;
            }

            if (NameofRules.IsInsideNameofArgument(use.Node))
            {
                return NameofReason;
            }

            string reason = EvaluateEventSkipReason(use.EventSymbol, compiledType);
            if (reason != null)
            {
                return reason;
            }
        }

        return null;
    }

    /// <summary>
    /// What: whether the body needs the event rewrite, which forces delegation even when nothing
    /// else in the body is inaccessible (a transplanted shim would not compile).
    /// </summary>
    internal static bool BodyRequiresEventAccessors(SyntaxNode bodyNode, SemanticModel semanticModel)
    {
        foreach (EventUse use in EnumerateEventUses(bodyNode, semanticModel))
        {
            if (!use.IsSubscription && !NameofRules.IsInsideNameofArgument(use.Node))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// What: whether an assignment writes the event's backing field (E = handler) rather than
    /// subscribing to it. += / -= keep the publicized add/remove accessors so the compiler's
    /// Interlocked.CompareExchange loop is preserved.
    /// </summary>
    internal static bool IsBackingFieldWrite(AssignmentExpressionSyntax assignment)
    {
        return assignment.IsKind(SyntaxKind.SimpleAssignmentExpression);
    }

    internal static bool IsSubscriptionAssignment(AssignmentExpressionSyntax assignment)
    {
        return assignment.IsKind(SyntaxKind.AddAssignmentExpression)
            || assignment.IsKind(SyntaxKind.SubtractAssignmentExpression);
    }

    private static string EvaluateEventSkipReason(IEventSymbol eventSymbol, INamedTypeSymbol compiledType)
    {
        if (eventSymbol.IsAbstract || eventSymbol.IsExtern || eventSymbol.ContainingType.TypeKind == TypeKind.Interface)
        {
            return NoBackingFieldReason;
        }

        // A custom add/remove event has no compiler-generated backing field to reach. C# also
        // rejects raising one from source (CS0079), so this is a guard, not a reachable path.
        if (HasCustomAccessors(eventSymbol))
        {
            return CustomAccessorReason;
        }

        if (!AccessibilityRules.IsExternallyVisibleType(eventSymbol.Type)
            || !AccessibilityRules.IsExternallyVisibleType(eventSymbol.ContainingType))
        {
            return DelegateTypeNotVisibleReason;
        }

        if (!CompiledEventExists(eventSymbol, compiledType))
        {
            return AddedInThisEditReason;
        }

        return null;
    }

    private static bool HasCustomAccessors(IEventSymbol eventSymbol)
    {
        foreach (SyntaxReference reference in eventSymbol.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax() is EventDeclarationSyntax)
            {
                return true;
            }
        }

        return false;
    }

    private static bool CompiledEventExists(IEventSymbol eventSymbol, INamedTypeSymbol compiledType)
    {
        // Raising is only legal inside the declaring type, so the event always belongs to the
        // type currently being emitted; anything else is treated as not proven present.
        if (compiledType == null)
        {
            return false;
        }

        foreach (ISymbol member in compiledType.GetMembers(eventSymbol.Name))
        {
            if (member is IEventSymbol compiledEvent && compiledEvent.IsStatic == eventSymbol.IsStatic)
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<EventUse> EnumerateEventUses(SyntaxNode bodyNode, SemanticModel semanticModel)
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

            bool isSubscription = effective.Parent is AssignmentExpressionSyntax assignment
                && IsSubscriptionAssignment(assignment)
                && assignment.Left == effective;
            yield return new EventUse(effective, eventSymbol, isSubscription);
        }
    }

    private readonly struct EventUse
    {
        internal EventUse(SyntaxNode node, IEventSymbol eventSymbol, bool isSubscription)
        {
            Node = node;
            EventSymbol = eventSymbol;
            IsSubscription = isSubscription;
        }

        internal SyntaxNode Node { get; }

        internal IEventSymbol EventSymbol { get; }

        internal bool IsSubscription { get; }
    }
}
