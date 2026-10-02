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
/// What: decides how a body's field-like event uses are handled — rewritten through the event's
/// backing field, left on the publicized add/remove accessors, or skipped.
/// </summary>
internal static class EventAccessorRules
{
    /// <summary>
    /// What: the skip reason for a body's event uses, or null when every use is a subscription
    /// (+= / -=) to an event the compiled assembly already has, rewritable through the backing
    /// field, or a use of an added event the added-field store keeps.
    /// </summary>
    internal static WorkerReason EvaluateEventUseSkipReason(
        SyntaxNode bodyNode,
        SemanticModel semanticModel,
        INamedTypeSymbol compiledType,
        AddedEventLookup addedEvents)
    {
        foreach (EventUse use in EnumerateEventUses(bodyNode, semanticModel))
        {
            WorkerReason reason = addedEvents.IsStoreBacked(use.EventSymbol)
                ? EvaluateStoreBackedUseSkipReason(use)
                : EvaluateAccessorUseSkipReason(use, compiledType, addedEvents);
            if (reason != null)
            {
                return reason;
            }
        }

        return null;
    }

    /// <summary>
    /// What: whether the body needs the event rewrite, which forces delegation even when nothing
    /// else in the body is inaccessible (a transplanted shim would not compile). An added event
    /// the store keeps does not: its uses become store calls any assembly can compile.
    /// </summary>
    internal static bool BodyRequiresEventAccessors(
        SyntaxNode bodyNode,
        SemanticModel semanticModel,
        AddedEventLookup addedEvents)
    {
        foreach (EventUse use in EnumerateEventUses(bodyNode, semanticModel))
        {
            if (!use.IsSubscription
                && !NameofRules.IsInsideNameofArgument(use.Node)
                && !addedEvents.IsStoreBacked(use.EventSymbol))
            {
                return true;
            }
        }

        return false;
    }

    // The store rewrite covers subscribing, raising, reading, and assigning; what is left are the
    // shapes it has no receiver or no variable for.
    private static WorkerReason EvaluateStoreBackedUseSkipReason(EventUse use)
    {
        // 'a?.E' has no receiver name the store call could take, for a subscription as for a read.
        if (use.Node is MemberBindingExpressionSyntax)
        {
            return WorkerReason.Of(HotReloadWorkerReasonCode.EventConditionalReceiver);
        }

        if (use.IsSubscription)
        {
            return null;
        }

        if (NameofRules.IsInsideNameofArgument(use.Node))
        {
            return WorkerReason.Of(HotReloadWorkerReasonCode.EventNameof);
        }

        // A store read is a call result, which C# cannot pass by reference.
        if (IsPassedByRef(use.Node))
        {
            return WorkerReason.Of(HotReloadWorkerReasonCode.EventPassedByRef);
        }

        return null;
    }

    private static WorkerReason EvaluateAccessorUseSkipReason(
        EventUse use,
        INamedTypeSymbol compiledType,
        AddedEventLookup addedEvents)
    {
        if (use.IsSubscription)
        {
            if (!addedEvents.IsAddedInThisEdit(use.EventSymbol))
            {
                return null;
            }

            // Why the visibility reason first: it is the one a reader can act on without a
            // compile, while the added-event reason only says the event is new.
            return DescribeInvisibleEvent(use.EventSymbol)
                ?? WorkerReason.Of(
                    HotReloadWorkerReasonCode.EventSubscriptionToAddedEvent,
                    use.EventSymbol.ContainingType.ToDisplayString() + "." + use.EventSymbol.Name);
        }

        if (NameofRules.IsInsideNameofArgument(use.Node))
        {
            return WorkerReason.Of(HotReloadWorkerReasonCode.EventNameof);
        }

        // 'a?.E' binds the event on a receiver the shim has no name for, so the accessor
        // call cannot be built and the raw event access would reach the shim source.
        if (use.Node is MemberBindingExpressionSyntax)
        {
            return WorkerReason.Of(HotReloadWorkerReasonCode.EventConditionalReceiver);
        }

        // The rewrite turns the event read into a cast of an accessor call, and C# cannot
        // pass that by reference, so the shim would fail to compile instead of skipping.
        if (IsPassedByRef(use.Node))
        {
            return WorkerReason.Of(HotReloadWorkerReasonCode.EventPassedByRef);
        }

        return EvaluateEventSkipReason(use.EventSymbol, compiledType);
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

    /// <summary>
    /// The kind of the '+=' or '-=' assignment whose handler the expression is, or
    /// SyntaxKind.None when the expression is not such a handler.
    /// </summary>
    internal static SyntaxKind FindHandlerAssignmentKind(ExpressionSyntax operand)
    {
        // Why parentheses and one cast are looked past: '-= (Handler)' and '-= (Action)Handler'
        // still remove the delegate the method group converts to.
        SyntaxNode unwrapped = operand;
        while (unwrapped.Parent is ParenthesizedExpressionSyntax parenthesized)
        {
            unwrapped = parenthesized;
        }

        if (unwrapped.Parent is CastExpressionSyntax cast && cast.Expression == unwrapped)
        {
            unwrapped = cast;
            while (unwrapped.Parent is ParenthesizedExpressionSyntax outer)
            {
                unwrapped = outer;
            }
        }

        if (unwrapped.Parent is not AssignmentExpressionSyntax assignment
            || assignment.Right != unwrapped
            || !IsSubscriptionAssignment(assignment))
        {
            return SyntaxKind.None;
        }

        return assignment.Kind();
    }

    private static bool IsPassedByRef(SyntaxNode eventUseNode)
    {
        // 'ref (E)' is still a by-ref argument, and the rewritten read inside the parentheses is
        // still not a variable, so the argument is looked up past any wrapping parentheses.
        SyntaxNode argumentExpression = eventUseNode;
        while (argumentExpression.Parent is ParenthesizedExpressionSyntax parenthesized)
        {
            argumentExpression = parenthesized;
        }

        return argumentExpression.Parent is ArgumentSyntax argument
            && argument.Expression == argumentExpression
            && !argument.RefKindKeyword.IsKind(SyntaxKind.None);
    }

    private static WorkerReason EvaluateEventSkipReason(IEventSymbol eventSymbol, INamedTypeSymbol compiledType)
    {
        if (eventSymbol.IsAbstract || eventSymbol.IsExtern || eventSymbol.ContainingType.TypeKind == TypeKind.Interface)
        {
            return WorkerReason.Of(HotReloadWorkerReasonCode.EventNoBackingField);
        }

        // A custom add/remove event has no compiler-generated backing field to reach. C# also
        // rejects raising one from source (CS0079), so this is a guard, not a reachable path.
        if (HasCustomAccessors(eventSymbol))
        {
            return WorkerReason.Of(HotReloadWorkerReasonCode.EventCustomAccessor);
        }

        WorkerReason invisible = DescribeInvisibleEvent(eventSymbol);
        if (invisible != null)
        {
            return invisible;
        }

        if (!CompiledBackingFieldExists(eventSymbol, compiledType))
        {
            return WorkerReason.Of(HotReloadWorkerReasonCode.EventAddedInThisEdit);
        }

        return null;
    }

    private static WorkerReason DescribeInvisibleEvent(IEventSymbol eventSymbol)
    {
        if (!AccessibilityRules.IsExternallyVisibleType(eventSymbol.Type)
            || !AccessibilityRules.IsExternallyVisibleType(eventSymbol.ContainingType))
        {
            return WorkerReason.Of(HotReloadWorkerReasonCode.EventDelegateTypeNotVisible);
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

    // Harmony looks the backing field up by the event's own name, so the compiled event must
    // still be field-like and carry the same delegate type. The field itself is not visible:
    // metadata symbols expose only accessible members, and the backing field stays private.
    // A compiler-generated add accessor is the metadata evidence that the field exists.
    private static bool CompiledBackingFieldExists(IEventSymbol eventSymbol, INamedTypeSymbol compiledType)
    {
        // Raising is only legal inside the declaring type, so the event always belongs to the
        // type currently being emitted; anything else is treated as not proven present.
        if (compiledType == null)
        {
            return false;
        }

        string eventTypeDisplay = eventSymbol.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        foreach (ISymbol member in compiledType.GetMembers(eventSymbol.Name))
        {
            if (member is not IEventSymbol compiledEvent
                || compiledEvent.IsStatic != eventSymbol.IsStatic
                || compiledEvent.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                    != eventTypeDisplay)
            {
                continue;
            }

            if (HasCompilerGeneratedAccessor(compiledEvent))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasCompilerGeneratedAccessor(IEventSymbol compiledEvent)
    {
        if (compiledEvent.AddMethod == null)
        {
            return false;
        }

        foreach (AttributeData attribute in compiledEvent.AddMethod.GetAttributes())
        {
            // Full name, not the short one: a user-defined CompilerGeneratedAttribute must not
            // pass for the framework marker the C# compiler emits on field-like accessors.
            if (attribute.AttributeClass != null
                && attribute.AttributeClass.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                    == "global::System.Runtime.CompilerServices.CompilerGeneratedAttribute")
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
            else if (node.Parent is MemberBindingExpressionSyntax parentBinding && parentBinding.Name == node)
            {
                effective = parentBinding;
            }

            // Why parentheses are looked past: '(E) += h' subscribes as 'E += h' does.
            SyntaxNode assignedSide = effective;
            while (assignedSide.Parent is ParenthesizedExpressionSyntax parenthesized)
            {
                assignedSide = parenthesized;
            }

            bool isSubscription = assignedSide.Parent is AssignmentExpressionSyntax assignment
                && IsSubscriptionAssignment(assignment)
                && assignment.Left == assignedSide;
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
