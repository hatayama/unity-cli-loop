using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// Decides which source declarations may be removed from the tree the transform binds against.
// A declaration is only removable when the artifact record that claims it still describes the
// source: same owner file, same metadata name, and the same fingerprint recomputed from the
// edited text. Otherwise the source is newer than the artifact and the source has to win.
internal static class IntroducedTypeDeclarationVerifier
{
    // The declarations of each unit that a retained artifact already serves, keyed by unit.
    internal static Dictionary<WorkerSourceUnit, List<BaseTypeDeclarationSyntax>> FindRetainedDeclarations(
        IReadOnlyList<WorkerSourceUnit> units,
        CSharpCompilation verificationCompilation,
        WorkerInput input,
        IAssemblySymbol targetAssembly,
        IntroducedTypeArtifactMap artifactMap)
    {
        Dictionary<string, WorkerIntroducedTypeArtifactType> recordsByKey = BuildRecordIndex(input);
        Dictionary<WorkerSourceUnit, List<BaseTypeDeclarationSyntax>> retainedDeclarations =
            new Dictionary<WorkerSourceUnit, List<BaseTypeDeclarationSyntax>>();
        if (recordsByKey.Count == 0)
        {
            return retainedDeclarations;
        }

        foreach (WorkerSourceUnit unit in units)
        {
            List<BaseTypeDeclarationSyntax> declarations = FindRetainedDeclarationsOfUnit(
                unit, verificationCompilation, input, targetAssembly, artifactMap, recordsByKey);
            if (declarations.Count > 0)
            {
                retainedDeclarations.Add(unit, declarations);
            }
        }

        return retainedDeclarations;
    }

    private static List<BaseTypeDeclarationSyntax> FindRetainedDeclarationsOfUnit(
        WorkerSourceUnit unit,
        CSharpCompilation verificationCompilation,
        WorkerInput input,
        IAssemblySymbol targetAssembly,
        IntroducedTypeArtifactMap artifactMap,
        Dictionary<string, WorkerIntroducedTypeArtifactType> recordsByKey)
    {
        List<BaseTypeDeclarationSyntax> declarations = new List<BaseTypeDeclarationSyntax>();
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
                unit.Root,
                declaration,
                input.Defines,
                typeSymbol,
                semanticModel,
                targetAssembly,
                input.TargetAssemblyName,
                input.TargetAssemblyMvid,
                artifactMap);
            if (string.Equals(fingerprint, record.DeclarationFingerprint, StringComparison.Ordinal))
            {
                declarations.Add(declaration);
            }
        }

        return declarations;
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
