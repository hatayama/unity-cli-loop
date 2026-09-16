using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// Reports an event accessor the shim cannot host as Skipped. Split from the other unsupported
// member kinds because an event carries two accessors that change independently, so each one is
// compared against its snapshot peer on its own.
internal static class EventAccessorSkipCollector
{
    internal static void AppendEventAccessorSkips(
        TypeDeclarationSyntax typeDeclaration,
        string typeMetadataNameFromSyntax,
        SemanticModel semanticModel,
        List<WorkerSkipped> skipped,
        Dictionary<string, EventDeclarationSyntax> snapshotEventMap,
        Dictionary<string, EventDeclarationSyntax> plainCurrentEventMap)
    {
        foreach (EventDeclarationSyntax eventDeclaration in typeDeclaration.Members
            .OfType<EventDeclarationSyntax>())
        {
            string eventKey = WorkerSyntaxIndex.BuildSyntaxEventKey(typeMetadataNameFromSyntax, eventDeclaration);
            EventDeclarationSyntax snapshotEvent = null;
            EventDeclarationSyntax plainEvent = null;
            bool hasSnapshotPeer = snapshotEventMap != null
                && plainCurrentEventMap != null
                && snapshotEventMap.TryGetValue(eventKey, out snapshotEvent)
                && plainCurrentEventMap.TryGetValue(eventKey, out plainEvent);
            if (hasSnapshotPeer
                && SyntaxFactory.AreEquivalent(snapshotEvent, plainEvent, topLevel: false))
            {
                continue;
            }

            IEventSymbol eventSymbol = semanticModel.GetDeclaredSymbol(eventDeclaration);
            if (eventSymbol == null)
            {
                continue;
            }

            EventDeclarationSyntax currentEvent = hasSnapshotPeer ? plainEvent : eventDeclaration;
            EventDeclarationSyntax snapshotForCompare = hasSnapshotPeer ? snapshotEvent : null;
            AppendEventAccessorSkipForKind(
                skipped,
                eventDeclaration,
                snapshotForCompare,
                currentEvent,
                SyntaxKind.AddAccessorDeclaration,
                eventSymbol.AddMethod);
            AppendEventAccessorSkipForKind(
                skipped,
                eventDeclaration,
                snapshotForCompare,
                currentEvent,
                SyntaxKind.RemoveAccessorDeclaration,
                eventSymbol.RemoveMethod);
        }
    }

    // Why per accessor: an add-only edit used to skip remove as well because the
    // collector compared the whole event declaration.
    private static void AppendEventAccessorSkipForKind(
        List<WorkerSkipped> skipped,
        EventDeclarationSyntax eventDeclaration,
        EventDeclarationSyntax snapshotEvent,
        EventDeclarationSyntax currentEvent,
        SyntaxKind accessorKind,
        IMethodSymbol accessorMethod)
    {
        if (snapshotEvent != null
            && EventAccessorUnchanged(snapshotEvent, currentEvent, accessorKind))
        {
            return;
        }

        AppendEventAccessorSkipIfExplicit(skipped, eventDeclaration, accessorKind, accessorMethod);
    }

    private static bool EventAccessorUnchanged(
        EventDeclarationSyntax snapshotEvent,
        EventDeclarationSyntax currentEvent,
        SyntaxKind accessorKind)
    {
        AccessorDeclarationSyntax snapshotAccessor = FindEventAccessor(snapshotEvent, accessorKind);
        AccessorDeclarationSyntax currentAccessor = FindEventAccessor(currentEvent, accessorKind);
        return snapshotAccessor != null
            && currentAccessor != null
            && SyntaxFactory.AreEquivalent(snapshotAccessor, currentAccessor, topLevel: false);
    }

    private static AccessorDeclarationSyntax FindEventAccessor(
        EventDeclarationSyntax eventDeclaration,
        SyntaxKind accessorKind)
    {
        if (eventDeclaration == null || eventDeclaration.AccessorList == null)
        {
            return null;
        }

        foreach (AccessorDeclarationSyntax accessor in eventDeclaration.AccessorList.Accessors)
        {
            if (accessor.Kind() == accessorKind)
            {
                return accessor;
            }
        }

        return null;
    }

    private static void AppendEventAccessorSkipIfExplicit(
        List<WorkerSkipped> skipped,
        EventDeclarationSyntax eventDeclaration,
        SyntaxKind accessorKind,
        IMethodSymbol accessorMethod)
    {
        if (accessorMethod == null || eventDeclaration.AccessorList == null)
        {
            return;
        }

        foreach (AccessorDeclarationSyntax accessor in eventDeclaration.AccessorList.Accessors)
        {
            if (accessor.Kind() != accessorKind)
            {
                continue;
            }

            if (accessor.Body == null && accessor.ExpressionBody == null)
            {
                return;
            }

            UnsupportedMemberSkipCollector.AppendUnsupportedKindSkip(skipped, accessorMethod);
            return;
        }
    }
}
