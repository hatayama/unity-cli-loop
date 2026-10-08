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

// Plans which newly written top-level declarations of one compilation assembly could be
// introduced without a compile, and refuses the run outright when the inputs the plan depends on
// could not be read or describe a different assembly than the request named.
internal static class IntroducedTypePreparation
{
    // The planning compilation itself when this run has no other edited file to read, so the
    // common case does not pay for a second compilation.
    private static CSharpCompilation CreateConstDriftCompilation(
        WorkerInput input,
        CSharpParseOptions parseOptions,
        List<SyntaxTree> syntaxTrees,
        List<MetadataReference> references,
        CSharpCompilation planningCompilation,
        IReadOnlyList<UsingDirectiveSyntax> assemblyGlobalUsings,
        IReadOnlyList<CompilationUnitSyntax> analyzableRoots)
    {
        List<SyntaxTree> siblingTrees = SiblingConstDriftCollector.ParseChangedSiblings(
            input.ChangedSiblingSourcePaths,
            parseOptions);
        if (siblingTrees.Count == 0)
        {
            return planningCompilation;
        }

        // Why a tree of its own rather than the planning one: a changed sibling is one of the
        // assembly's other files, so the global usings it declares are already among
        // assemblyGlobalUsings, and its own tree now joins the compilation as well.
        List<CompilationUnitSyntax> allRoots = new List<CompilationUnitSyntax>(analyzableRoots);
        foreach (SyntaxTree siblingTree in siblingTrees)
        {
            allRoots.Add(siblingTree.GetCompilationUnitRoot());
        }

        List<SyntaxTree> allTrees = new List<SyntaxTree>(syntaxTrees.Count + siblingTrees.Count + 1);
        allTrees.AddRange(syntaxTrees);
        allTrees.AddRange(siblingTrees);
        SyntaxTree globalUsingTree = WorkerGlobalUsingBindingTree.Build(assemblyGlobalUsings, allRoots, parseOptions);
        if (globalUsingTree != null)
        {
            allTrees.Add(globalUsingTree);
        }

        return CSharpCompilation.Create(
            assemblyName: "UloopHotReloadIntroducedTypeConstVerification",
            syntaxTrees: allTrees,
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    internal static WorkerOutput Prepare(WorkerInput input, WorkerRequestTimings timings)
    {
        CSharpParseOptions parseOptions = new CSharpParseOptions(
            languageVersion: LanguageVersion.Latest,
            preprocessorSymbols: input.Defines);
        List<WorkerSourceUnit> units = new List<WorkerSourceUnit>(input.Sources.Length);
        List<SyntaxTree> syntaxTrees = new List<SyntaxTree>(input.Sources.Length);
        List<WorkerSourceUnit> analyzableUnits = new List<WorkerSourceUnit>(input.Sources.Length);
        foreach (WorkerSourceInput source in input.Sources)
        {
            WorkerSourceUnit unit = WorkerSourceLoader.Load(source, parseOptions);
            units.Add(unit);
            if (unit.SyntaxTree != null && unit.ParseErrors.Count == 0)
            {
                syntaxTrees.Add(unit.SyntaxTree);
                analyzableUnits.Add(unit);
            }
        }

        timings.Lap("parse_sources");
        List<string> referenceParseErrors = new List<string>();
        (List<MetadataReference> references, MetadataReference targetTypesReference) =
            WorkerGroupPipeline.CollectMetadataReferences(input, referenceParseErrors);
        List<(WorkerIntroducedTypeArtifact Artifact, MetadataReference Reference)> artifactReferences =
            IntroducedTypeArtifactReferences.Collect(input, references, referenceParseErrors);
        timings.Lap("collect_references");
        List<CompilationUnitSyntax> analyzableRoots = new List<CompilationUnitSyntax>(analyzableUnits.Count);
        foreach (WorkerSourceUnit analyzableUnit in analyzableUnits)
        {
            analyzableRoots.Add(analyzableUnit.Root);
        }

        // Why collected before planning: a base or member type the source reaches only through
        // another file's global using must bind in the planning compilation too, or a Unity object
        // ancestor stays an unresolved error type and the declaration is planned instead of refused.
        List<UsingDirectiveSyntax> assemblyGlobalUsings =
            WorkerUsingCollector.CollectAssemblyGlobalUsings(input, parseOptions, analyzableRoots);
        SyntaxTree globalUsingTree = WorkerGlobalUsingBindingTree.Build(assemblyGlobalUsings, analyzableRoots, parseOptions);
        timings.Lap("global_usings");
        // Why appended only here and not to syntaxTrees: the const drift compilation adds the
        // changed siblings and builds a tree of its own, which would otherwise repeat directives.
        CSharpCompilation compilation = CSharpCompilation.Create(
            assemblyName: "UloopHotReloadIntroducedTypePlanning",
            syntaxTrees: WorkerGlobalUsingBindingTree.Append(syntaxTrees, globalUsingTree),
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        timings.Lap("create_compilation");
        AppendUnreadableReferenceErrors(compilation, references, referenceParseErrors);
        IAssemblySymbol targetAssembly =
            WorkerCompiledAssemblySymbols.ResolveWithAllMembers(compilation, targetTypesReference);
        WorkerTypeHome home = new WorkerTypeHome(input.TargetAssemblyName, targetAssembly);
        WorkerReason incompleteInputsDiagnostic =
            DescribeIncompleteCompilationInputs(input, targetAssembly, referenceParseErrors);
        IntroducedTypeArtifactMap artifactMap = IntroducedTypeArtifactMap.Empty;
        if (incompleteInputsDiagnostic == null
            && !IntroducedTypeArtifactMap.TryBuild(compilation, artifactReferences, out artifactMap, out string artifactError))
        {
            // Why the sentence still comes from the worker here: the same artifact error is also
            // returned as a fatal transform message, so it stays one sentence owned by the map.
            incompleteInputsDiagnostic = WorkerReason.Of(
                HotReloadWorkerReasonCode.IntroducedTypeArtifactUnusable,
                artifactError);
        }
        // Why a second compilation: a const declared in a file this run does not transform binds
        // to the value the target assembly was compiled with, so its edited value is invisible in
        // the planning compilation and a changed const would pass unnoticed. Planning itself stays
        // on the compilation of the requested sources only, so which types are introduced does not
        // depend on which other files happened to be edited.
        CSharpCompilation constDriftCompilation = CreateConstDriftCompilation(
            input,
            parseOptions,
            syntaxTrees,
            references,
            compilation,
            assemblyGlobalUsings,
            analyzableRoots);
        AddedMemberReferenceClassifier addedMemberClassifier = new AddedMemberReferenceClassifier(
            compilation,
            home,
            artifactMap,
            input.TargetAssemblyMvid);
        WorkerFileOutput[] files = new WorkerFileOutput[units.Count];
        string[][] plannedAddedMemberNames = new string[units.Count][];
        string[][] plannedAddedEnumMemberNames = new string[units.Count][];
        string[][] declarationDriftWarnings = new string[units.Count][];
        for (int index = 0; index < units.Count; index++)
        {
            WorkerSourceUnit unit = units[index];
            if (unit.SyntaxTree != null && unit.ParseErrors.Count == 0)
            {
                if (incompleteInputsDiagnostic == null)
                {
                    unit.SemanticModel = compilation.GetSemanticModel(unit.SyntaxTree, ignoreAccessibility: false);
                    unit.ConstDriftSemanticModel = constDriftCompilation.GetSemanticModel(
                        unit.SyntaxTree,
                        ignoreAccessibility: false);
                    IntroducedTypePlanner.Plan(
                        unit,
                        home,
                        input.TargetAssemblyMvid,
                        artifactMap,
                        input.Defines,
                        assemblyGlobalUsings,
                        addedMemberClassifier);
                    plannedAddedMemberNames[index] = PlannedAddedMemberNames.Collect(
                        unit,
                        home,
                        compilation,
                        artifactMap,
                        input.TargetAssemblyName,
                        input.TargetAssemblyMvid);
                    plannedAddedEnumMemberNames[index] = PlannedAddedMemberNames.CollectCompiledEnumMembers(
                        unit.Root,
                        unit.SemanticModel,
                        home);
                    // Why collected here as well: a refused or failed introduced-type batch ends
                    // the run before the transform run, which is what reports these otherwise.
                    declarationDriftWarnings[index] = ConstDriftCollector.CollectConstDriftWarnings(
                        unit.Root,
                        unit.ConstDriftSemanticModel,
                        home,
                        new HashSet<string>(StringComparer.Ordinal)).ToArray();
                }
                else
                {
                    unit.IntroducedTypeDiagnostics.Add(incompleteInputsDiagnostic);
                }
            }

            unit.ParseErrors.AddRange(referenceParseErrors);
            files[index] = new WorkerFileOutput
            {
                ProjectRelativePath = unit.Input.ProjectRelativePath,
                SourceContentSha256 = unit.SourceContentSha256,
                ParseErrors = unit.ParseErrors.ToArray(),
                DeclarationDriftWarnings = Array.Empty<string>(),
                RemovedMembers = Array.Empty<WorkerRemovedMember>(),
                RemovedMethodSignatures = Array.Empty<WorkerRemovedMethodSignature>(),
                AddedFieldNames = Array.Empty<string>(),
                AddedFieldInitializers = Array.Empty<string>(),
                AddedFieldDeclarations = Array.Empty<WorkerAddedFieldDeclaration>(),
                AddedConstNames = Array.Empty<string>(),
                AddedEnumMemberNames = Array.Empty<string>(),
                IntroducedTypes = unit.IntroducedTypes.ToArray(),
                IntroducedTypeDiagnostics = unit.IntroducedTypeDiagnostics.ToArray(),
                IntroducedTypeReuses = unit.IntroducedTypeReuses.ToArray(),
                PlannedAddedMemberNames = plannedAddedMemberNames[index] ?? Array.Empty<string>(),
                PlannedAddedEnumMemberNames = plannedAddedEnumMemberNames[index] ?? Array.Empty<string>(),
                PreparedDeclarationDriftWarnings = declarationDriftWarnings[index] ?? Array.Empty<string>()
            };
        }

        return new WorkerOutput
        {
            ShimSource = string.Empty,
            Entries = Array.Empty<WorkerEntry>(),
            Skipped = Array.Empty<WorkerSkipped>(),
            Files = files,
            ParseErrors = Array.Empty<string>(),
            SiblingConstDriftWarnings = Array.Empty<string>(),
            UnchangedMethods = Array.Empty<WorkerUnchangedMethod>()
        };
    }

    // The reason planning cannot run, or null when every input the plan depends on was readable
    // and describes the assembly the request named.
    private static WorkerReason DescribeIncompleteCompilationInputs(
        WorkerInput input,
        IAssemblySymbol targetAssembly,
        List<string> referenceParseErrors)
    {
        // Planning decides a declaration is new by failing to find it in the target assembly, so
        // an unresolved target symbol would make every type already in that assembly look newly
        // introduced, and a reference that failed to parse would settle the supported-boundary
        // questions against an incomplete picture. Neither may produce a descriptor; the file
        // carries the reason instead.
        if (targetAssembly == null || referenceParseErrors.Count > 0)
        {
            return WorkerReason.Of(HotReloadWorkerReasonCode.IntroducedTypeInputsUnreadable);
        }

        // Every descriptor carries the requested identity, and a retained artifact is only valid
        // for the assembly generation it was planned against. Reading the identity back from the
        // file that was actually analysed is what stops a stale request from producing artifacts
        // that claim an assembly the planning never looked at.
        if (!IntroducedTypeTargetIdentity.MatchesRequest(input, targetAssembly))
        {
            return WorkerReason.Of(HotReloadWorkerReasonCode.IntroducedTypeIdentityMismatch);
        }

        return null;
    }

    // A reference file that exists but is not readable managed metadata never reaches the
    // compilation, so the boundary checks would bind against error types and answer "not a Unity
    // object", "not serializable" and the like for declarations nothing could classify.
    private static void AppendUnreadableReferenceErrors(
        CSharpCompilation compilation,
        List<MetadataReference> references,
        List<string> parseErrors)
    {
        foreach (MetadataReference reference in references)
        {
            if (compilation.GetAssemblyOrModuleSymbol(reference) != null)
            {
                continue;
            }

            // A reference the compilation dropped because another reference already supplied the
            // same assembly identity also answers null here, so the file itself is read before it
            // is called unreadable.
            if (CanReadAssemblyMetadata(reference.Display))
            {
                continue;
            }

            parseErrors.Add("Reference could not be read: " + reference.Display);
        }
    }

    private static bool CanReadAssemblyMetadata(string assemblyPath)
    {
        if (string.IsNullOrEmpty(assemblyPath))
        {
            return false;
        }

        try
        {
            using (AssemblyMetadata metadata = AssemblyMetadata.CreateFromFile(assemblyPath))
            {
                return metadata.GetModules().Length > 0;
            }
        }
        catch (BadImageFormatException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }
}
