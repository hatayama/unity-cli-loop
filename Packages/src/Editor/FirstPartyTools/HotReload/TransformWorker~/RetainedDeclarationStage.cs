using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// The stage that decides which declarations of a group a retained artifact already serves, and
// what the transform does with each of them: a declaration the artifact still matches exactly
// leaves the trees the transform binds against, because leaving it in would bind callers to the
// source definition instead of the assembly an earlier reload retained; one whose ordinary method
// bodies alone changed stays, so those bodies can be transformed and patched onto that assembly.
// It runs before anything is emitted, for the same reason.
internal static class RetainedDeclarationStage
{
    // Removes from each unit's binding tree the declarations a retained artifact already serves
    // unchanged, records the ones whose method bodies this edit changed, and reports why the
    // artifacts could not be used at all. Returns null when the run has no artifact to bind
    // against, which is every run until introduced types are in play.
    internal static string PrepareBindingTrees(
        WorkerInput input,
        List<WorkerSourceUnit> loadedUnits,
        List<MetadataReference> references,
        MetadataReference targetTypesReference,
        CSharpParseOptions parseOptions)
    {
        if (input.IntroducedTypeArtifacts.Length == 0)
        {
            return null;
        }

        List<string> artifactErrors = new List<string>();
        List<(WorkerIntroducedTypeArtifact Artifact, MetadataReference Reference)> artifactReferences =
            IntroducedTypeArtifactReferences.Collect(input, references, artifactErrors);
        if (artifactErrors.Count > 0)
        {
            return artifactErrors[0];
        }

        List<SyntaxTree> editedTrees = new List<SyntaxTree>(loadedUnits.Count);
        foreach (WorkerSourceUnit loadedUnit in loadedUnits)
        {
            editedTrees.Add(loadedUnit.SyntaxTree);
        }

        // The declarations are verified against the edited text, so this compilation binds the
        // trees as written; the binding compilation is built from what survives the removal.
        CSharpCompilation verificationCompilation = CSharpCompilation.Create(
            assemblyName: "UloopHotReloadRetainedDeclarationVerification",
            syntaxTrees: editedTrees,
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        IAssemblySymbol targetAssembly = WorkerCompiledAssemblySymbols.ResolveWithAllMembers(
            verificationCompilation,
            targetTypesReference);

        // A record is only valid for the assembly generation it was planned against, and the
        // recorded identities are what the fingerprint is rebuilt from. Binding against an
        // assembly the request cannot name would rebuild them from an empty identity, no
        // declaration would match its record, and every retained type would quietly bind from
        // source again.
        if (!IntroducedTypeTargetIdentity.MatchesRequest(input, targetAssembly))
        {
            return "Retained introduced types require the assembly identity the records were planned against.";
        }

        if (!IntroducedTypeArtifactMap.TryBuild(
                verificationCompilation, artifactReferences, out IntroducedTypeArtifactMap artifactMap, out string artifactError))
        {
            return artifactError;
        }

        foreach (WorkerSourceUnit loadedUnit in loadedUnits)
        {
            loadedUnit.ArtifactMap = artifactMap;
        }
        Dictionary<WorkerSourceUnit, List<RetainedDeclarationVerdict>> verdicts =
            IntroducedTypeDeclarationVerifier.FindRetainedDeclarations(
                loadedUnits, verificationCompilation, input, targetAssembly, artifactMap);
        string splitRefusal = RetainedTypeSignatureReferenceFinder.FindRefusal(
            verdicts, verificationCompilation, input, targetAssembly, artifactMap);
        if (splitRefusal != null)
        {
            return splitRefusal;
        }

        List<string> bindingParseErrors = new List<string>();
        foreach (KeyValuePair<WorkerSourceUnit, List<RetainedDeclarationVerdict>> entry in verdicts)
        {
            IntroducedTypeBindingRewriter.RemoveRetainedDeclarations(
                entry.Key, ApplyVerdicts(entry.Key, entry.Value), parseOptions, bindingParseErrors);
        }

        if (bindingParseErrors.Count > 0)
        {
            return bindingParseErrors[0];
        }

        PublishRunRetainedBodyEditTypes(loadedUnits);
        return null;
    }

    // Hands every unit the body-edit declarations of the whole run, because a file that only
    // constructs such a type declares none of them itself and would otherwise read the source
    // declaration the binding tree still holds as a type no artifact serves.
    private static void PublishRunRetainedBodyEditTypes(List<WorkerSourceUnit> loadedUnits)
    {
        List<WorkerRetainedBodyEditType> runBodyEditTypes = new List<WorkerRetainedBodyEditType>();
        foreach (WorkerSourceUnit loadedUnit in loadedUnits)
        {
            runBodyEditTypes.AddRange(loadedUnit.RetainedBodyEditTypes);
        }

        foreach (WorkerSourceUnit loadedUnit in loadedUnits)
        {
            loadedUnit.RunRetainedBodyEditTypes = runBodyEditTypes;
        }
    }

    // Records what the unit's verdicts mean for it and returns the declarations to remove. The
    // recording happens before the removal, because the rewriter replaces the unit's tree and
    // these declarations belong to the tree it replaces.
    private static List<BaseTypeDeclarationSyntax> ApplyVerdicts(
        WorkerSourceUnit unit,
        List<RetainedDeclarationVerdict> verdicts)
    {
        List<BaseTypeDeclarationSyntax> removable = new List<BaseTypeDeclarationSyntax>();
        foreach (RetainedDeclarationVerdict verdict in verdicts)
        {
            if (verdict.Match.Kind == IntroducedTypeFingerprintMatchKind.Identical)
            {
                RecordRetainedMetadataName(unit, verdict.Declaration);
                removable.Add(verdict.Declaration);
                continue;
            }

            // A difference the artifact cannot be brought up to leaves the declaration to the
            // ordinary path, which finds the type absent from the patch target and says so.
            // Reporting it as retained instead would claim the artifact still runs a definition
            // it does not. Added members are not such a difference: the artifact still runs
            // every member it was built with, and the new ones are emitted onto it the way they
            // are emitted onto a compiled type.
            if (verdict.Match.Kind != IntroducedTypeFingerprintMatchKind.MethodBodiesOnly
                && verdict.Match.Kind != IntroducedTypeFingerprintMatchKind.MembersAdded)
            {
                continue;
            }

            RecordRetainedMetadataName(unit, verdict.Declaration);
            unit.RetainedBodyEditTypes.Add(new WorkerRetainedBodyEditType
            {
                MetadataName = verdict.MetadataName,
                OriginalAssemblyName = verdict.Record.OriginalAssemblyName,
                OriginalAssemblyMvid = verdict.Record.OriginalAssemblyMvid,
                ChangedMethodKeys = verdict.Match.ChangedMethodSyntaxKeys.ToArray(),
                ChangedGetterPropertyKeys = verdict.Match.ChangedGetterPropertySyntaxKeys.ToArray()
            });
        }

        return removable;
    }

    // Built from the syntax rather than the symbol: the drift check strips by the syntax key of
    // the edited root.
    private static void RecordRetainedMetadataName(WorkerSourceUnit unit, BaseTypeDeclarationSyntax declaration)
    {
        if (declaration is TypeDeclarationSyntax typeDeclaration)
        {
            unit.RetainedIntroducedTypeMetadataNames.Add(
                WorkerSyntaxIndex.BuildTypeMetadataNameFromSyntax(typeDeclaration));
            return;
        }

        if (declaration is EnumDeclarationSyntax enumDeclaration)
        {
            unit.RetainedIntroducedTypeMetadataNames.Add(
                WorkerSyntaxIndex.BuildEnumMetadataNameFromSyntax(enumDeclaration));
        }
    }
}
