using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using io.github.hatayama.UnityCliLoop.FirstPartyTools;

/// <summary>
/// Skips a method whose signature does not resolve because another part of its partial type was not
/// bound, with the partial-parts reason instead of classifying it as an ordinary added method.
/// </summary>
internal static class UnboundPartialSignatureSkip
{
    /// <summary>
    /// True when the method was skipped: its signature names an unresolved type and another part of
    /// its partial type is untrusted. A method the snapshot lacks is still recorded as added.
    /// </summary>
    internal static bool TrySkip(
        MethodDeclarationSyntax methodDeclaration,
        IMethodSymbol methodSymbol,
        string methodKey,
        TypeEmitState typeState,
        Dictionary<string, MethodDeclarationSyntax> snapshotMethodMap,
        AddedMethodCatalog addedMethodCatalog,
        List<WorkerSkipped> skipped)
    {
        if (!TrySkipWithUntrustedPartReason(methodDeclaration, methodSymbol, typeState, skipped))
        {
            return false;
        }

        RecordGenuinelyAddedMethod(typeState, methodDeclaration, methodKey, snapshotMethodMap, addedMethodCatalog);
        return true;
    }

    // Why not added: when another part of the partial type was not bound (it changed, or the scan
    // that would prove it unchanged did not finish), a signature naming a type from that part does
    // not resolve, so the compiled method looks missing. Calling it added gave the wrong reason and
    // recorded an added key that the drift check strips from the current tree only, which reported
    // an outside-body edit for a file that had none.
    private static bool TrySkipWithUntrustedPartReason(
        MethodDeclarationSyntax methodDeclaration,
        IMethodSymbol methodSymbol,
        TypeEmitState typeState,
        List<WorkerSkipped> skipped)
    {
        if (!(methodDeclaration.Parent is TypeDeclarationSyntax typeDeclaration)
            || !UnresolvedSignatureTypes.TryFindInSignature(methodSymbol, out ITypeSymbol _))
        {
            return false;
        }

        WorkerReason untrustedPart = typeState.SourceUnit.PartialTypeParts.FindUntrustedOtherPartReasonOrNull(typeDeclaration);
        if (untrustedPart == null)
        {
            return false;
        }

        skipped.Add(new WorkerSkipped
        {
            SourceProjectRelativePath = typeState.SourceUnit.Input.ProjectRelativePath,
            Method = WorkerMethodKeys.FormatRowLabel(typeState.CompiledType, methodSymbol),
            Reason = untrustedPart
        });
        return true;
    }

    // A method the snapshot does not hold was really added. It is classified added, so a patched
    // caller of it is skipped rather than calling a method the running assembly lacks, and its
    // syntax key is recorded, so the drift check does not report the addition its skip row explains.
    // Without a snapshot nothing tells the two apart, so the method is classified added, the safe
    // side for callers. A method the snapshot holds is the compiled one seen through unbound parts:
    // nothing is recorded, so both trees keep it and its callers bind to the compiled method.
    private static void RecordGenuinelyAddedMethod(
        TypeEmitState typeState,
        MethodDeclarationSyntax methodDeclaration,
        string methodKey,
        Dictionary<string, MethodDeclarationSyntax> snapshotMethodMap,
        AddedMethodCatalog addedMethodCatalog)
    {
        if (snapshotMethodMap == null)
        {
            addedMethodCatalog.MarkClassifiedAdded(methodKey);
            return;
        }

        string syntaxMethodKey = WorkerSyntaxIndex.BuildSyntaxMethodKey(typeState.TypeMetadataNameFromSyntax, methodDeclaration);
        if (snapshotMethodMap.ContainsKey(syntaxMethodKey))
        {
            return;
        }

        addedMethodCatalog.MarkClassifiedAdded(methodKey);
        addedMethodCatalog.AddAddedSyntaxKey(syntaxMethodKey);
    }
}
