// Hot-reload transform worker: parse + semantic analysis of one edited C# file, emit static
// shim method sources (no Prefix wrappers) plus a per-method manifest / skip list.
// Runs out-of-process on the Unity-bundled .NET host against the Unity-bundled Roslyn.
// Generated shims mirror user method signatures verbatim; repo style rules apply to
// hand-written code only.

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

public static class TransformWorkerProgram
{
    private const string RoslynDirectorySidecarFileName = "roslyn-directory.txt";

    // SyntaxAnnotation kind for original-source 1-based lines; survive rewriter + NormalizeWhitespace
    // so Emit can inject #line directives after formatting.
    internal const string UloopLineAnnotationKind = "uloop-line";

    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    public static int Main(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine("usage: TransformWorker <input-json-path> <output-json-path>");
            return 2;
        }

        string roslynDirectoryPath = ReadRoslynDirectorySidecar();
        AssemblyLoadContext.Default.Resolving += (context, assemblyName) =>
        {
            string candidatePath = Path.Combine(roslynDirectoryPath, assemblyName.Name + ".dll");
            if (File.Exists(candidatePath))
            {
                return context.LoadFromAssemblyPath(candidatePath);
            }

            return null;
        };

        return RunTransform(args[0], args[1]);
    }

    private static string ReadRoslynDirectorySidecar()
    {
        string baseDirectory = AppContext.BaseDirectory;
        string sidecarPath = Path.Combine(baseDirectory, RoslynDirectorySidecarFileName);
        if (!File.Exists(sidecarPath))
        {
            throw new InvalidOperationException(
                "Roslyn directory sidecar not found next to worker.dll: " + sidecarPath);
        }

        string roslynDirectoryPath = File.ReadAllText(sidecarPath, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)).Trim();
        if (string.IsNullOrEmpty(roslynDirectoryPath) || !Directory.Exists(roslynDirectoryPath))
        {
            throw new InvalidOperationException("Roslyn directory from sidecar is missing: " + roslynDirectoryPath);
        }

        return roslynDirectoryPath;
    }

    private static int RunTransform(string inputJsonPath, string outputJsonPath)
    {
        WorkerInput input = ReadInput(inputJsonPath);
        WorkerOutput output = TransformFile(input);
        WriteOutput(outputJsonPath, output);
        return 0;
    }

    private static WorkerInput ReadInput(string inputJsonPath)
    {
        byte[] bytes = File.ReadAllBytes(inputJsonPath);
        WorkerInput input = JsonSerializer.Deserialize<WorkerInput>(bytes, JsonOptions);
        if (input == null)
        {
            throw new InvalidOperationException("Failed to deserialize worker input JSON.");
        }

        if (string.IsNullOrEmpty(input.SourcePath))
        {
            throw new InvalidOperationException("sourcePath is required.");
        }

        input.Defines ??= Array.Empty<string>();
        input.ReferencePaths ??= Array.Empty<string>();
        input.ExcludedMethodKeys ??= Array.Empty<string>();
        input.ExcludedAddedMethodKeys ??= Array.Empty<string>();
        input.AssemblySourcePaths ??= Array.Empty<string>();
        return input;
    }

    private static void WriteOutput(string outputJsonPath, WorkerOutput output)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(output, JsonOptions);
        File.WriteAllBytes(outputJsonPath, bytes);
    }

    private static WorkerOutput TransformFile(WorkerInput input)
    {
        WorkerOutput invalidPath = TryCreateInvalidPathOutput(input);
        if (invalidPath != null)
        {
            return invalidPath;
        }

        (WorkerOutput readFailure, string sourceText, string sourceContentSha256) = TryReadSourceText(input);
        if (readFailure != null)
        {
            return readFailure;
        }

        List<string> parseErrors = new List<string>();
        CSharpParseOptions parseOptions = new CSharpParseOptions(
            languageVersion: LanguageVersion.Latest,
            preprocessorSymbols: input.Defines);
        (SyntaxTree syntaxTree, CompilationUnitSyntax plainRoot) = WorkerSourceAnnotator.ParseAndAnnotateSource(
            sourceText,
            parseOptions,
            input.SourcePath,
            parseErrors);

        (List<MetadataReference> references, MetadataReference targetTypesReference) =
            CollectMetadataReferences(input, parseErrors);

        CSharpCompilation compilation = CSharpCompilation.Create(
            assemblyName: "UloopHotReloadTransformWorkerCompilation",
            syntaxTrees: new[] { syntaxTree },
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        SemanticModel semanticModel = compilation.GetSemanticModel(syntaxTree, ignoreAccessibility: true);
        CompilationUnitSyntax root = syntaxTree.GetCompilationUnitRoot();

        IAssemblySymbol targetTypesAssemblySymbol = ResolveTargetTypesAssemblySymbol(
            compilation,
            targetTypesReference);
        List<string> declarationDriftWarnings = ConstDriftCollector.CollectConstDriftWarnings(
            root,
            semanticModel,
            targetTypesAssemblySymbol);
        // Why here: a compiled property/event can disappear or change kind with no
        // touched body, so the generic outside-body warning would bury the name.
        CompiledMemberKindChangeWarnings.AppendCompiledPropertyOrEventKindChangeWarnings(
            root,
            semanticModel,
            targetTypesAssemblySymbol,
            declarationDriftWarnings);

        BaselineSnapshotState baseline = BaselineSnapshotBuilder.BuildBaselineSnapshotState(input, parseOptions, plainRoot);

        List<WorkerEntry> entries = new List<WorkerEntry>();
        List<WorkerSkipped> skipped = new List<WorkerSkipped>();
        List<WorkerUnchangedMethod> unchangedMethods = new List<WorkerUnchangedMethod>();
        List<WorkerRemovedMember> removedMembers = new List<WorkerRemovedMember>();
        List<WorkerRemovedMethodSignature> removedMethodSignatures = new List<WorkerRemovedMethodSignature>();
        List<ShimTypeBuilder> shimTypes = new List<ShimTypeBuilder>();
        int globalShimMethodCounter = 0;
        int shimTypeCounter = 0;
        List<UsingDirectiveSyntax> assemblyGlobalUsings =
            WorkerUsingCollector.CollectAssemblyGlobalUsings(input, parseOptions);
        AddedMethodCatalog addedMethodCatalog = new AddedMethodCatalog();
        AddedFieldCatalog addedFieldCatalog = new AddedFieldCatalog();
        (List<TypeEmitState> typeEmitStates, int nextShimTypeCounter, int nextGlobalShimMethodCounter) =
            TypeEmitPlanner.QueueAllTypeEmitStates(
                root,
                semanticModel,
                targetTypesAssemblySymbol,
                input,
                baseline,
                assemblyGlobalUsings,
                shimTypes,
                addedMethodCatalog,
                addedFieldCatalog,
                skipped,
                unchangedMethods,
                declarationDriftWarnings,
                removedMembers,
                removedMethodSignatures,
                shimTypeCounter,
                globalShimMethodCounter);
        shimTypeCounter = nextShimTypeCounter;
        globalShimMethodCounter = nextGlobalShimMethodCounter;

        RemovedMemberCollector.CollectRemovedMembersIfBaseline(
            baseline,
            plainRoot,
            typeEmitStates,
            semanticModel,
            targetTypesAssemblySymbol,
            addedMethodCatalog,
            addedFieldCatalog,
            removedMembers,
            removedMethodSignatures);

        AddedCallSiteGuard.SkipBodiesThatCannotUseAddedMethods(
            typeEmitStates,
            semanticModel,
            addedMethodCatalog,
            addedFieldCatalog,
            skipped);

        ShimMethodEmitter.EmitQueuedMethodsAndPropertyGetters(
            typeEmitStates,
            semanticModel,
            addedMethodCatalog,
            addedFieldCatalog,
            root,
            input,
            baseline,
            entries,
            skipped,
            unchangedMethods,
            shimTypes,
            assemblyGlobalUsings,
            shimTypeCounter,
            globalShimMethodCounter);

        if (baseline.HasBaseline && baseline.SnapshotRoot != null)
        {
            // Why after property emit: added-property syntax keys are registered when a skip
            // row is written. Running the drift check first would miss those keys and keep
            // the false outside-body warning for added properties that already have a row.
            OutsideMethodBodyDriftChecker.AppendOutsideMethodBodyDriftWarningIfNeeded(
                baseline.SnapshotRoot,
                plainRoot,
                Path.GetFileName(input.SourcePath),
                declarationDriftWarnings,
                addedMethodCatalog,
                addedFieldCatalog);
        }

        return BuildWorkerOutput(
            root,
            input.ProjectRelativePath,
            shimTypes,
            entries,
            skipped,
            declarationDriftWarnings,
            parseErrors,
            unchangedMethods,
            baseline,
            removedMembers,
            removedMethodSignatures,
            addedFieldCatalog,
            sourceContentSha256);
    }

    private static WorkerOutput TryCreateInvalidPathOutput(WorkerInput input)
    {
        // Why ParseErrors (not Debug.Assert): ProjectRelativePath crosses a process boundary via
        // JSON, and the worker is built without a DEBUG define so Conditional Asserts are stripped.
        if (string.IsNullOrEmpty(input.ProjectRelativePath)
            || input.ProjectRelativePath.IndexOf('\\') >= 0
            || input.ProjectRelativePath.IndexOf('"') >= 0)
        {
            return new WorkerOutput
            {
                ShimSource = string.Empty,
                Entries = Array.Empty<WorkerEntry>(),
                Skipped = Array.Empty<WorkerSkipped>(),
                ParseErrors = new[]
                {
                    "Invalid projectRelativePath: must be a non-empty forward-slash path without quotes."
                }
            };
        }

        return null;
    }

    private static (WorkerOutput Failure, string SourceText, string SourceContentSha256) TryReadSourceText(
        WorkerInput input)
    {
        try
        {
            byte[] sourceBytes = File.ReadAllBytes(input.SourcePath);
            string sourceContentSha256 = ComputeSourceContentSha256(sourceBytes);
            using MemoryStream memoryStream = new MemoryStream(sourceBytes, writable: false);
            using StreamReader reader = new StreamReader(
                memoryStream,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true);
            return (null, reader.ReadToEnd(), sourceContentSha256);
        }
        catch (Exception exception)
        {
            return (
                new WorkerOutput
                {
                    ShimSource = string.Empty,
                    Entries = Array.Empty<WorkerEntry>(),
                    Skipped = Array.Empty<WorkerSkipped>(),
                    ParseErrors = new[] { "Failed to read sourcePath: " + exception.Message }
                },
                null,
                null);
        }
    }

    private static (List<MetadataReference> References, MetadataReference TargetTypesReference)
        CollectMetadataReferences(WorkerInput input, List<string> parseErrors)
    {
        string targetTypesFullPath =
            !string.IsNullOrEmpty(input.TargetTypesAssemblyPath) && File.Exists(input.TargetTypesAssemblyPath)
                ? Path.GetFullPath(input.TargetTypesAssemblyPath)
                : null;
        MetadataReference targetTypesReference = null;

        List<MetadataReference> references = new List<MetadataReference>();
        foreach (string referencePath in input.ReferencePaths)
        {
            if (File.Exists(referencePath))
            {
                MetadataReference reference = MetadataReference.CreateFromFile(referencePath);
                references.Add(reference);
                if (targetTypesFullPath != null
                    && string.Equals(
                        Path.GetFullPath(referencePath),
                        targetTypesFullPath,
                        StringComparison.OrdinalIgnoreCase))
                {
                    targetTypesReference = reference;
                }
            }
            else
            {
                parseErrors.Add("Reference not found: " + referencePath);
            }
        }

        if (targetTypesFullPath != null && targetTypesReference == null)
        {
            targetTypesReference = MetadataReference.CreateFromFile(input.TargetTypesAssemblyPath);
            references.Add(targetTypesReference);
        }

        return (references, targetTypesReference);
    }

    private static IAssemblySymbol ResolveTargetTypesAssemblySymbol(
        CSharpCompilation compilation,
        MetadataReference targetTypesReference)
    {
        // The drift comparison must see private and internal consts in the compiled target
        // assembly, which the default MetadataImportOptions (Public) hides. Widening the main
        // compilation would also widen what every classification query can bind to, so the
        // wider import is confined to a throwaway compilation used only for this lookup.
        if (targetTypesReference == null)
        {
            return null;
        }

        CSharpCompilation driftCompilation = compilation.WithOptions(
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithMetadataImportOptions(MetadataImportOptions.All));
        return driftCompilation.GetAssemblyOrModuleSymbol(targetTypesReference) as IAssemblySymbol;
    }

    private static WorkerOutput BuildWorkerOutput(
        CompilationUnitSyntax root,
        string projectRelativePath,
        List<ShimTypeBuilder> shimTypes,
        List<WorkerEntry> entries,
        List<WorkerSkipped> skipped,
        List<string> declarationDriftWarnings,
        List<string> parseErrors,
        List<WorkerUnchangedMethod> unchangedMethods,
        BaselineSnapshotState baseline,
        List<WorkerRemovedMember> removedMembers,
        List<WorkerRemovedMethodSignature> removedMethodSignatures,
        AddedFieldCatalog addedFieldCatalog,
        string sourceContentSha256)
    {
        bool hasAccessorDelegates = false;
        foreach (ShimTypeBuilder shimType in shimTypes)
        {
            if (shimType.AccessorPlan.Entries.Count > 0)
            {
                hasAccessorDelegates = true;
                break;
            }
        }

        string shimSource = ShimSourceEmitter.Emit(root, shimTypes, projectRelativePath);
        return new WorkerOutput
        {
            ShimSource = shimSource,
            Entries = entries.ToArray(),
            Skipped = skipped.ToArray(),
            DeclarationDriftWarnings = declarationDriftWarnings.ToArray(),
            ParseErrors = parseErrors.ToArray(),
            UnchangedMethods = unchangedMethods.ToArray(),
            BaselineDisabledByDuplicateKeys = baseline.BaselineDisabledByDuplicateKeys,
            RemovedMembers = removedMembers.ToArray(),
            RemovedMethodSignatures = removedMethodSignatures.ToArray(),
            HasAccessorDelegates = hasAccessorDelegates,
            HasAddedFieldRewrites = addedFieldCatalog.HasStoreRewrites,
            AddedFieldNames = addedFieldCatalog.ListRewrittenAddedFieldDisplayNames(),
            SourceContentSha256 = sourceContentSha256
        };
    }

    // Keep in sync with HotReloadAppliedSourceLedger.ComputeContentHash (lowercase hex SHA-256).
    private static string ComputeSourceContentSha256(byte[] bytes)
    {
        using SHA256 sha256 = SHA256.Create();
        byte[] hash = sha256.ComputeHash(bytes);
        StringBuilder builder = new StringBuilder(hash.Length * 2);
        for (int index = 0; index < hash.Length; index++)
        {
            builder.Append(hash[index].ToString("x2"));
        }

        return builder.ToString();
    }

    private static IEnumerable<TypeDeclarationSyntax> EnumerateTypeDeclarations(CompilationUnitSyntax root)
    {
        // Why interfaces: a new interface (including default methods) is absent from the compiled
        // assembly; enumerating it registers the type syntax key so the strip rewriter can drop
        // it instead of firing a false outside-body drift warning.
        return root.DescendantNodes()
            .OfType<TypeDeclarationSyntax>()
            .Where(static typeDeclaration =>
                typeDeclaration is ClassDeclarationSyntax
                || typeDeclaration is StructDeclarationSyntax
                || typeDeclaration is RecordDeclarationSyntax
                || typeDeclaration is InterfaceDeclarationSyntax);
    }
}
