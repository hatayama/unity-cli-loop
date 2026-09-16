using System.Collections.Generic;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using io.github.hatayama.UnityCliLoop.FirstPartyTools;

// Reads the fingerprint recorded for the artifact this domain already serves a declaration from
// and turns it into the prepare stage's decision: reuse the artifact, or refuse with the reason
// the Editor asks for a compile with. Kept out of the planner so the planner only decides which
// declarations are candidates at all.
internal static class IntroducedTypeReuseDecider
{
    // Whether this domain already retains an assembly for the declaration. Introducing it again
    // would compile a second artifact for the same original type, and the transform run would
    // then be offered two records normalizing to it, which it refuses. The declaration binds
    // from the active record instead, so this run introduces nothing.
    internal static bool IsAlreadyIntroduced(
        WorkerSourceUnit unit,
        IntroducedTypeArtifactMap artifactMap,
        string targetAssemblyName,
        string targetAssemblyMvid,
        string metadataName,
        string declarationFingerprint,
        BaseTypeDeclarationSyntax declaration)
    {
        string activeFingerprint = artifactMap.FindActiveDeclarationFingerprint(
            targetAssemblyName,
            targetAssemblyMvid,
            metadataName);
        if (activeFingerprint == null)
        {
            return false;
        }

        IntroducedTypeFingerprintMatch match = IntroducedTypeFingerprintMatch.Classify(
            activeFingerprint,
            declarationFingerprint,
            IntroducedTypeDeclarationMemberIndex.Build(declaration, metadataName));
        if (match.Kind == IntroducedTypeFingerprintMatchKind.OtherBodiesChanged)
        {
            unit.IntroducedTypeDiagnostics.Add(
                WorkerReason.CompositeWithArgs(
                    HotReloadWorkerReasonCode.IntroducedTypeMemberBodyChanged,
                    DescribeDifferences(match.ChangedOtherKeys),
                    metadataName));
            return true;
        }

        // Replacing the retained assembly a live type was loaded from is outside what a reload
        // can do, so a redefined introduced type is reported instead of being introduced again.
        if (match.Kind == IntroducedTypeFingerprintMatchKind.DeclarationChanged
            || match.Kind == IntroducedTypeFingerprintMatchKind.RecordUnreadable)
        {
            // The editor side recognises this diagnostic by its code and reads the type name out
            // of the first value, so the wording it renders is free to change on that side alone.
            unit.IntroducedTypeDiagnostics.Add(
                WorkerReason.CompositeWithArgs(
                    HotReloadWorkerReasonCode.IntroducedTypeChanged,
                    DescribeDifferences(match.Details),
                    metadataName));
            return true;
        }

        // Why recorded: the run binds this declaration from the active artifact, and without a
        // record the reload could not tell that from a run that never saw the declaration. Added
        // members need no mark of their own here: the transform stage reads them off the artifact
        // the same way it reads them off a compiled type.
        unit.IntroducedTypeReuses.Add(
            new WorkerIntroducedTypeReuse
            {
                MetadataName = metadataName,
                OriginalAssemblyName = targetAssemblyName ?? string.Empty,
                OriginalAssemblyMvid = targetAssemblyMvid ?? string.Empty,
                BodyEdited = match.ChangedMethodSyntaxKeys.Count > 0
            });
        return true;
    }

    // The list of differences the refusal ends with. The worker joins it because the tokens are
    // member keys and fingerprint part names, not English the Editor has to word.
    private static WorkerReason DescribeDifferences(IReadOnlyList<string> differences)
    {
        return WorkerReason.Of(
            HotReloadWorkerReasonCode.IntroducedTypeDifferenceList,
            string.Join(", ", differences));
    }
}
