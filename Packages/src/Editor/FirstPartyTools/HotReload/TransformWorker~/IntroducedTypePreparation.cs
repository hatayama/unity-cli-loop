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

// Plans which newly written top-level declarations of one compilation assembly could be
// introduced without a compile, and refuses the run outright when the inputs the plan depends on
// could not be read or describe a different assembly than the request named.
internal static class IntroducedTypePreparation
{
    internal static WorkerOutput Prepare(WorkerInput input)
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

        List<string> referenceParseErrors = new List<string>();
        (List<MetadataReference> references, MetadataReference targetTypesReference) =
            WorkerGroupPipeline.CollectMetadataReferences(input, referenceParseErrors);
        List<(WorkerIntroducedTypeArtifact Artifact, MetadataReference Reference)> artifactReferences =
            IntroducedTypeArtifactReferences.Collect(input, references, referenceParseErrors);
        CSharpCompilation compilation = CSharpCompilation.Create(
            assemblyName: "UloopHotReloadIntroducedTypePlanning",
            syntaxTrees: syntaxTrees,
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        AppendUnreadableReferenceErrors(compilation, references, referenceParseErrors);
        IAssemblySymbol targetAssembly = WorkerGroupPipeline.ResolveTargetTypesAssemblySymbol(compilation, targetTypesReference);
        List<CompilationUnitSyntax> analyzableRoots = new List<CompilationUnitSyntax>(analyzableUnits.Count);
        foreach (WorkerSourceUnit analyzableUnit in analyzableUnits)
        {
            analyzableRoots.Add(analyzableUnit.Root);
        }

        List<UsingDirectiveSyntax> assemblyGlobalUsings =
            WorkerUsingCollector.CollectAssemblyGlobalUsings(input, parseOptions, analyzableRoots);
        string incompleteInputsDiagnostic =
            DescribeIncompleteCompilationInputs(input, targetAssembly, referenceParseErrors);
        IntroducedTypeArtifactMap artifactMap = IntroducedTypeArtifactMap.Empty;
        if (incompleteInputsDiagnostic == null
            && !IntroducedTypeArtifactMap.TryBuild(compilation, artifactReferences, out artifactMap, out string artifactError))
        {
            incompleteInputsDiagnostic = "Introduced types require a compile: " + artifactError;
        }
        WorkerFileOutput[] files = new WorkerFileOutput[units.Count];
        for (int index = 0; index < units.Count; index++)
        {
            WorkerSourceUnit unit = units[index];
            if (unit.SyntaxTree != null && unit.ParseErrors.Count == 0)
            {
                if (incompleteInputsDiagnostic == null)
                {
                    unit.SemanticModel = compilation.GetSemanticModel(unit.SyntaxTree, ignoreAccessibility: false);
                    IntroducedTypePlanner.Plan(
                        unit,
                        targetAssembly,
                        input.TargetAssemblyName,
                        input.TargetAssemblyMvid,
                        artifactMap,
                        input.Defines,
                        assemblyGlobalUsings);
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
                AddedConstNames = Array.Empty<string>(),
                IntroducedTypes = unit.IntroducedTypes.ToArray(),
                IntroducedTypeDiagnostics = unit.IntroducedTypeDiagnostics.ToArray()
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
    private static string DescribeIncompleteCompilationInputs(
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
            return "Introduced types require a compile: the target assembly or its references could not be read.";
        }

        // Every descriptor carries the requested identity, and a retained artifact is only valid
        // for the assembly generation it was planned against. Reading the identity back from the
        // file that was actually analysed is what stops a stale request from producing artifacts
        // that claim an assembly the planning never looked at.
        if (!TargetAssemblyIdentityMatchesRequest(input, targetAssembly))
        {
            return "Introduced types require a compile: the target assembly identity does not match the request.";
        }

        return null;
    }

    private static bool TargetAssemblyIdentityMatchesRequest(WorkerInput input, IAssemblySymbol targetAssembly)
    {
        // Assembly names are compared case-insensitively by the runtime, so a case difference in
        // the request is the same assembly and must not reject the plan.
        if (!string.Equals(
                input.TargetAssemblyName,
                targetAssembly.Identity.Name,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!Guid.TryParse(input.TargetAssemblyMvid, out Guid requestedMvid) || requestedMvid == Guid.Empty)
        {
            return false;
        }

        return requestedMvid == ReadModuleVersionId(input.TargetTypesAssemblyPath);
    }

    private static Guid ReadModuleVersionId(string assemblyPath)
    {
        if (string.IsNullOrEmpty(assemblyPath))
        {
            return Guid.Empty;
        }

        try
        {
            using (ModuleMetadata metadata = ModuleMetadata.CreateFromFile(assemblyPath))
            {
                return metadata.GetModuleVersionId();
            }
        }
        catch (BadImageFormatException)
        {
            return Guid.Empty;
        }
        catch (IOException)
        {
            return Guid.Empty;
        }
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
