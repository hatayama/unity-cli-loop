using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using io.github.hatayama.UnityCliLoop.FirstPartyTools;

/// <summary>
/// Skips a method whose signature does not resolve because another part of its partial type was not
/// bound, with the partial-parts reason instead of classifying it as an ordinary added method.
/// </summary>
internal static class UnboundPartialSignatureSkip
{
    /// <summary>
    /// True when the method was skipped: its signature names an unresolved type, another part of
    /// its partial type is untrusted, and its declaration is not a real change from the snapshot's.
    /// A method the snapshot lacks is still recorded as added. False leaves the method to the
    /// ordinary added-method path.
    /// </summary>
    internal static bool TrySkip(
        MethodDeclarationSyntax methodDeclaration,
        IMethodSymbol methodSymbol,
        string methodKey,
        TypeEmitState typeState,
        Dictionary<string, MethodDeclarationSyntax> snapshotMethodMap,
        Dictionary<string, MethodDeclarationSyntax> plainCurrentMethodMap,
        AddedMethodCatalog addedMethodCatalog,
        List<WorkerSkipped> skipped)
    {
        WorkerReason untrustedPart = FindUntrustedPartReasonOrNull(methodDeclaration, methodSymbol, typeState);
        if (untrustedPart == null)
        {
            return false;
        }

        string syntaxMethodKey = WorkerSyntaxIndex.BuildSyntaxMethodKey(typeState.TypeMetadataNameFromSyntax, methodDeclaration);
        SnapshotStanding standing = ClassifyAgainstSnapshot(syntaxMethodKey, snapshotMethodMap, plainCurrentMethodMap);
        if (standing == SnapshotStanding.DeclarationChanged)
        {
            return false;
        }

        skipped.Add(new WorkerSkipped
        {
            SourceProjectRelativePath = typeState.SourceUnit.Input.ProjectRelativePath,
            Method = WorkerMethodKeys.FormatRowLabel(typeState.CompiledType, methodSymbol),
            Reason = untrustedPart
        });
        RecordGenuinelyAddedMethod(standing, syntaxMethodKey, methodKey, addedMethodCatalog);
        return true;
    }

    // Why not added: when another part of the partial type was not bound (it changed, or the scan
    // that would prove it unchanged did not finish), a signature naming a type from that part does
    // not resolve, so the compiled method looks missing. Calling it added gave the wrong reason and
    // recorded an added key that the drift check strips from the current tree only, which reported
    // an outside-body edit for a file that had none.
    private static WorkerReason FindUntrustedPartReasonOrNull(
        MethodDeclarationSyntax methodDeclaration,
        IMethodSymbol methodSymbol,
        TypeEmitState typeState)
    {
        if (!(methodDeclaration.Parent is TypeDeclarationSyntax typeDeclaration)
            || !UnresolvedSignatureTypes.TryFindInSignature(methodSymbol, out ITypeSymbol _))
        {
            return null;
        }

        return typeState.SourceUnit.PartialTypeParts.FindUntrustedOtherPartReasonOrNull(typeDeclaration);
    }

    // Why compare the declaration outside the body: the syntax key holds only the name and the
    // parameter types, so a key the snapshot holds does not prove the method is the compiled one.
    // A changed return type or a changed modifier keeps the key, and such a method must stay on the
    // added path so its callers are guarded. Only an unchanged declaration is the compiled method
    // seen through unbound parts.
    private static SnapshotStanding ClassifyAgainstSnapshot(
        string syntaxMethodKey,
        Dictionary<string, MethodDeclarationSyntax> snapshotMethodMap,
        Dictionary<string, MethodDeclarationSyntax> plainCurrentMethodMap)
    {
        if (snapshotMethodMap == null)
        {
            return SnapshotStanding.NoSnapshot;
        }

        if (!snapshotMethodMap.TryGetValue(syntaxMethodKey, out MethodDeclarationSyntax snapshotMethod))
        {
            return SnapshotStanding.AbsentFromSnapshot;
        }

        if (plainCurrentMethodMap == null
            || !plainCurrentMethodMap.TryGetValue(syntaxMethodKey, out MethodDeclarationSyntax currentMethod)
            || !SyntaxFactory.AreEquivalent(WithoutBody(snapshotMethod), WithoutBody(currentMethod), topLevel: false))
        {
            return SnapshotStanding.DeclarationChanged;
        }

        return SnapshotStanding.DeclarationUnchanged;
    }

    private static MethodDeclarationSyntax WithoutBody(MethodDeclarationSyntax method)
    {
        return method
            .WithBody(null)
            .WithExpressionBody(null)
            .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken))
            .NormalizeWhitespace();
    }

    // A method the snapshot does not hold was really added. It is classified added, so a patched
    // caller of it is skipped rather than calling a method the running assembly lacks, and its
    // syntax key is recorded, so the drift check does not report the addition its skip row explains.
    // Without a snapshot nothing tells the two apart, so the method is classified added, the safe
    // side for callers. An unchanged declaration is the compiled method seen through unbound parts:
    // nothing is recorded, so both trees keep it and its callers bind to the compiled method.
    private static void RecordGenuinelyAddedMethod(
        SnapshotStanding standing,
        string syntaxMethodKey,
        string methodKey,
        AddedMethodCatalog addedMethodCatalog)
    {
        if (standing == SnapshotStanding.DeclarationUnchanged)
        {
            return;
        }

        addedMethodCatalog.MarkClassifiedAdded(methodKey);
        if (standing == SnapshotStanding.AbsentFromSnapshot)
        {
            addedMethodCatalog.AddAddedSyntaxKey(syntaxMethodKey);
        }
    }

    private enum SnapshotStanding
    {
        NoSnapshot,
        AbsentFromSnapshot,
        DeclarationChanged,
        DeclarationUnchanged
    }
}
