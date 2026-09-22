using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// Reports how each source declaration compares to the artifact record that claims it: same owner
// file, same metadata name, and the fingerprint recomputed from the edited text against the
// recorded one. Only a declaration the record still describes exactly may leave the tree the
// transform binds against; one whose method bodies alone changed has to stay so those bodies can
// be transformed, and any wider difference means the source is newer and the source has to win.
internal static class IntroducedTypeDeclarationVerifier
{
    // The verdicts of each unit whose declarations a retained artifact claims, keyed by unit.
    internal static Dictionary<WorkerSourceUnit, List<RetainedDeclarationVerdict>> FindRetainedDeclarations(
        IReadOnlyList<WorkerSourceUnit> units,
        CSharpCompilation verificationCompilation,
        WorkerInput input,
        IAssemblySymbol targetAssembly,
        IntroducedTypeArtifactMap artifactMap)
    {
        Dictionary<string, WorkerIntroducedTypeArtifactType> recordsByKey = BuildRecordIndex(input);
        Dictionary<WorkerSourceUnit, List<RetainedDeclarationVerdict>> verdicts =
            new Dictionary<WorkerSourceUnit, List<RetainedDeclarationVerdict>>();
        if (recordsByKey.Count == 0)
        {
            return verdicts;
        }

        foreach (WorkerSourceUnit unit in units)
        {
            List<RetainedDeclarationVerdict> unitVerdicts = FindRetainedDeclarationsOfUnit(
                unit, verificationCompilation, input, targetAssembly, artifactMap, recordsByKey);
            if (unitVerdicts.Count > 0)
            {
                verdicts.Add(unit, unitVerdicts);
            }
        }

        return verdicts;
    }

    private static List<RetainedDeclarationVerdict> FindRetainedDeclarationsOfUnit(
        WorkerSourceUnit unit,
        CSharpCompilation verificationCompilation,
        WorkerInput input,
        IAssemblySymbol targetAssembly,
        IntroducedTypeArtifactMap artifactMap,
        Dictionary<string, WorkerIntroducedTypeArtifactType> recordsByKey)
    {
        List<RetainedDeclarationVerdict> verdicts = new List<RetainedDeclarationVerdict>();
        SemanticModel semanticModel = verificationCompilation.GetSemanticModel(unit.SyntaxTree, ignoreAccessibility: false);
        foreach (BaseTypeDeclarationSyntax declaration in unit.Root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
        {
            INamedTypeSymbol typeSymbol = semanticModel.GetDeclaredSymbol(declaration);
            if (typeSymbol == null || typeSymbol.ContainingType != null)
            {
                continue;
            }

            string metadataName = CecilTypeNames.ToMetadataName(typeSymbol);
            if (!recordsByKey.TryGetValue(
                    BuildKey(unit.Input.ProjectRelativePath, metadataName),
                    out WorkerIntroducedTypeArtifactType record))
            {
                continue;
            }

            // Recomputed through the same path the plan used, artifact normalization included:
            // a retained declaration may itself depend on another retained type, and computing
            // its fingerprint without the mapping would never reproduce the recorded value.
            string fingerprint = IntroducedTypeFingerprint.Compute(
                declaration,
                input.Defines,
                typeSymbol,
                semanticModel,
                targetAssembly,
                input.TargetAssemblyName,
                input.TargetAssemblyMvid,
                artifactMap).Serialize();

            // Classified through the shared reader rather than compared here, so this stage and
            // the planner cannot disagree about whether one edit is a body edit.
            IntroducedTypeFingerprintMatch match = IntroducedTypeFingerprintMatch.Classify(
                record.DeclarationFingerprint,
                fingerprint,
                IntroducedTypeDeclarationMemberIndex.Build(declaration, metadataName));
            verdicts.Add(new RetainedDeclarationVerdict(
                declaration,
                metadataName,
                match,
                record,
                HoldsOnlyAppliedChanges(unit, record)));
        }

        return verdicts;
    }

    // The record carries the owner file's hash only when the last reload applied all of it, so
    // bytes that still match hold nothing that reload did not already load.
    private static bool HoldsOnlyAppliedChanges(WorkerSourceUnit unit, WorkerIntroducedTypeArtifactType record)
    {
        return !string.IsNullOrEmpty(record.OwnerAppliedSourceHash)
            && string.Equals(record.OwnerAppliedSourceHash, unit.SourceContentSha256, StringComparison.Ordinal);
    }

    private static Dictionary<string, WorkerIntroducedTypeArtifactType> BuildRecordIndex(WorkerInput input)
    {
        Dictionary<string, WorkerIntroducedTypeArtifactType> recordsByKey =
            new Dictionary<string, WorkerIntroducedTypeArtifactType>(StringComparer.Ordinal);
        foreach (WorkerIntroducedTypeArtifact artifact in input.IntroducedTypeArtifacts)
        {
            if (artifact == null)
            {
                continue;
            }

            foreach (WorkerIntroducedTypeArtifactType artifactType in artifact.Types)
            {
                if (artifactType == null
                    || string.IsNullOrEmpty(artifactType.OwnerProjectRelativePath)
                    || string.IsNullOrEmpty(artifactType.DeclarationFingerprint))
                {
                    continue;
                }

                recordsByKey[BuildKey(artifactType.OwnerProjectRelativePath, artifactType.MetadataName)] =
                    artifactType;
            }
        }

        return recordsByKey;
    }

    private static string BuildKey(string ownerProjectRelativePath, string metadataName)
    {
        return (ownerProjectRelativePath ?? string.Empty) + "|" + (metadataName ?? string.Empty);
    }
}
