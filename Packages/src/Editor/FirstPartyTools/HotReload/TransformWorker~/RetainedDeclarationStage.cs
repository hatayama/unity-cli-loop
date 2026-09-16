using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// The stage that decides which declarations of a group a retained artifact already serves, and
// takes them out of the trees the transform binds against. It runs before anything is emitted,
// because a declaration left in place would bind callers to the source definition instead of the
// assembly an earlier reload retained.
internal static class RetainedDeclarationStage
{
    // Removes from each unit's binding tree the declarations a retained artifact already serves,
    // and reports why the artifacts could not be used at all. Returns null when the run has no
    // artifact to bind against, which is every run until introduced types are in play.
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
        IAssemblySymbol targetAssembly = WorkerGroupPipeline.ResolveTargetTypesAssemblySymbol(
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
        Dictionary<WorkerSourceUnit, List<BaseTypeDeclarationSyntax>> retainedDeclarations =
            IntroducedTypeDeclarationVerifier.FindRetainedDeclarations(
                loadedUnits, verificationCompilation, input, targetAssembly, artifactMap);
        List<string> bindingParseErrors = new List<string>();
        foreach (KeyValuePair<WorkerSourceUnit, List<BaseTypeDeclarationSyntax>> entry in retainedDeclarations)
        {
            // Recorded before the removal, because the rewriter replaces the unit's tree and
            // these declarations belong to the tree it replaces. Built from the syntax rather
            // than the symbol: the drift check strips by the syntax key of the edited root.
            foreach (BaseTypeDeclarationSyntax declaration in entry.Value)
            {
                if (declaration is TypeDeclarationSyntax typeDeclaration)
                {
                    entry.Key.RetainedIntroducedTypeMetadataNames.Add(
                        WorkerSyntaxIndex.BuildTypeMetadataNameFromSyntax(typeDeclaration));
                }
                else if (declaration is EnumDeclarationSyntax enumDeclaration)
                {
                    entry.Key.RetainedIntroducedTypeMetadataNames.Add(
                        WorkerSyntaxIndex.BuildEnumMetadataNameFromSyntax(enumDeclaration));
                }
            }

            IntroducedTypeBindingRewriter.RemoveRetainedDeclarations(
                entry.Key, entry.Value, parseOptions, bindingParseErrors);
        }

        if (bindingParseErrors.Count > 0)
        {
            return bindingParseErrors[0];
        }

        return null;
    }
}
