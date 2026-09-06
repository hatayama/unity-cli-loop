using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using Mono.Cecil;

using UnityEditor.Compilation;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Compiles immutable type declarations into a retained artifact without publishing it active.
    /// </summary>
    internal sealed class HotReloadIntroducedTypeCompiler
    {
        private readonly IHotReloadIntroducedTypeCompilerEnvironment environment;

        public HotReloadIntroducedTypeCompiler(IHotReloadIntroducedTypeCompilerEnvironment environment)
        {
            this.environment = environment ?? throw new ArgumentNullException(nameof(environment));
        }

        public async Task<HotReloadIntroducedTypeCompilerResult> CompileAsync(
            HotReloadIntroducedTypeCompilationRequest request,
            CancellationToken ct)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            ExternalCompilerPaths paths = await environment.ResolveCompilerPathsOnMainThreadAsync(ct)
                .ConfigureAwait(false);
            if (paths == null)
            {
                return HotReloadIntroducedTypeCompilerResult.Failure(
                    "External compiler paths could not be resolved for this Unity installation.");
            }

            foreach (HotReloadIntroducedTypeSource source in request.Sources)
            {
                environment.WriteSource(source.Path, source.Text);
            }

            DynamicCompilationBackendResult backendResult = await environment.CompileAsync(request, paths, ct)
                .ConfigureAwait(false);
            HotReloadIntroducedTypeCompilerResult backendFailure = ValidateBackendResult(request, backendResult);
            if (backendFailure != null)
            {
                return backendFailure;
            }

            HotReloadIntroducedTypeCompilerResult outputFailure = ValidateOutput(request);
            if (outputFailure != null)
            {
                return outputFailure;
            }

            ct.ThrowIfCancellationRequested();
            byte[] assemblyBytes = environment.ReadAllBytes(request.DllPath);
            byte[] pdbBytes = environment.ReadAllBytes(request.PdbPath);
            ct.ThrowIfCancellationRequested();
            CompiledAssemblyLoadResult loadResult = environment.Load(assemblyBytes, pdbBytes);
            if (!loadResult.Success || loadResult.CompiledAssembly == null)
            {
                return HotReloadIntroducedTypeCompilerResult.Failure(
                    "Introduced-type artifact failed to load.");
            }

            HotReloadIntroducedTypeArtifact artifact = new HotReloadIntroducedTypeArtifact(
                loadResult.CompiledAssembly,
                request.DllPath,
                request.PdbPath,
                request.Descriptors);
            return HotReloadIntroducedTypeCompilerResult.Prepared(artifact);
        }

        private HotReloadIntroducedTypeCompilerResult ValidateBackendResult(
            HotReloadIntroducedTypeCompilationRequest request,
            DynamicCompilationBackendResult backendResult)
        {
            if (backendResult == null)
            {
                return HotReloadIntroducedTypeCompilerResult.Failure("Introduced-type compilation produced no result.");
            }

            if (backendResult.BackendKind == DynamicCompilationBackendKind.AssemblyBuilderFallback)
            {
                return HotReloadIntroducedTypeCompilerResult.Failure(
                    "Introduced-type compilation requires the Roslyn compiler backend.");
            }

            return HasErrors(backendResult.CompilerMessages)
                ? HotReloadIntroducedTypeCompilerResult.Failure(
                    "Introduced-type compilation reported errors.",
                    CreateDiagnostics(request.Sources, backendResult.CompilerMessages))
                : null;
        }

        private HotReloadIntroducedTypeCompilerResult ValidateOutput(
            HotReloadIntroducedTypeCompilationRequest request)
        {
            if (!environment.FileExists(request.DllPath) || !environment.FileExists(request.PdbPath))
            {
                return HotReloadIntroducedTypeCompilerResult.Failure(
                    "Introduced-type compilation did not produce both DLL and PDB files.");
            }

            AssemblyName assemblyName = environment.ReadAssemblyName(request.DllPath);
            if (assemblyName == null || assemblyName.FullName != request.ExpectedAssemblyFullName)
            {
                return HotReloadIntroducedTypeCompilerResult.Failure(
                    "Introduced-type artifact identity does not match the requested assembly identity. Expected: "
                    + request.ExpectedAssemblyFullName + "; actual: "
                    + (assemblyName == null ? "(missing)" : assemblyName.FullName));
            }

            IReadOnlyCollection<string> emittedTypeNames = environment.ReadDefinedTypeNames(request.DllPath);
            foreach (HotReloadIntroducedTypeDescriptor descriptor in request.Descriptors)
            {
                if (!emittedTypeNames.Contains(descriptor.MetadataName))
                {
                    return HotReloadIntroducedTypeCompilerResult.Failure(
                        "Introduced-type artifact does not define every requested type.");
                }
            }

            return null;
        }

        private static bool HasErrors(CompilerMessage[] messages)
        {
            if (messages == null)
            {
                return false;
            }

            foreach (CompilerMessage message in messages)
            {
                if (message.type == CompilerMessageType.Error)
                {
                    return true;
                }
            }

            return false;
        }

        private static IReadOnlyList<HotReloadIntroducedTypeCompilerDiagnostic> CreateDiagnostics(
            IReadOnlyList<HotReloadIntroducedTypeSource> sources,
            CompilerMessage[] messages)
        {
            List<HotReloadIntroducedTypeCompilerDiagnostic> diagnostics =
                new List<HotReloadIntroducedTypeCompilerDiagnostic>();
            if (messages == null)
            {
                return diagnostics;
            }

            foreach (CompilerMessage message in messages)
            {
                if (message.type != CompilerMessageType.Error)
                {
                    continue;
                }

                string ownerProjectRelativePath = string.Empty;
                string diagnosticPath = string.IsNullOrWhiteSpace(message.file)
                    ? string.Empty
                    : Path.GetFullPath(message.file);
                foreach (HotReloadIntroducedTypeSource source in sources)
                {
                    if (string.Equals(source.Path, diagnosticPath, StringComparison.Ordinal))
                    {
                        ownerProjectRelativePath = source.Descriptor.OwnerProjectRelativePath;
                        break;
                    }
                }

                diagnostics.Add(new HotReloadIntroducedTypeCompilerDiagnostic(ownerProjectRelativePath, message.message));
            }

            return diagnostics;
        }
    }

    /// <summary>
    /// Defines the side-effect boundary of introduced-type compilation for deterministic tests.
    /// </summary>
    internal interface IHotReloadIntroducedTypeCompilerEnvironment
    {
        Task<ExternalCompilerPaths> ResolveCompilerPathsOnMainThreadAsync(CancellationToken ct);

        Task<DynamicCompilationBackendResult> CompileAsync(
            HotReloadIntroducedTypeCompilationRequest request,
            ExternalCompilerPaths paths,
            CancellationToken ct);

        bool FileExists(string path);

        AssemblyName ReadAssemblyName(string path);

        byte[] ReadAllBytes(string path);

        CompiledAssemblyLoadResult Load(byte[] assemblyBytes, byte[] pdbBytes);

        IReadOnlyCollection<string> ReadDefinedTypeNames(string path);

        void WriteSource(string path, string source);
    }

    /// <summary>
    /// Supplies all immutable inputs and output paths for one artifact compilation attempt.
    /// </summary>
    internal sealed class HotReloadIntroducedTypeCompilationRequest
    {
        public IReadOnlyList<HotReloadIntroducedTypeSource> Sources { get; }

        public string DllPath { get; }

        public string PdbPath { get; }

        public string ExpectedAssemblyFullName { get; }

        public IReadOnlyList<HotReloadIntroducedTypeDescriptor> Descriptors { get; }

        public IReadOnlyList<string> ReferencePaths { get; }

        public IReadOnlyList<string> DefineSymbols { get; }

        public HotReloadIntroducedTypeCompilationRequest(
            IReadOnlyList<HotReloadIntroducedTypeSource> sources,
            string dllPath,
            string pdbPath,
            string expectedAssemblyFullName,
            IReadOnlyList<HotReloadIntroducedTypeDescriptor> descriptors,
            IReadOnlyList<string> referencePaths,
            IReadOnlyList<string> defineSymbols)
        {
            Sources = CopySources(sources);
            DllPath = dllPath;
            PdbPath = pdbPath;
            ExpectedAssemblyFullName = expectedAssemblyFullName;
            Descriptors = CopyDescriptors(descriptors);
            ReferencePaths = CopyStrings(referencePaths);
            DefineSymbols = CopyStrings(defineSymbols);
        }

        public static HotReloadIntroducedTypeCompilationRequest CreateBatch(
            HotReloadIntroducedTypeArtifactPaths paths,
            IReadOnlyList<HotReloadIntroducedTypeDescriptor> descriptors,
            IReadOnlyList<string> referencePaths,
            IReadOnlyList<string> defineSymbols)
        {
            if (paths == null)
            {
                throw new ArgumentNullException(nameof(paths));
            }

            List<HotReloadIntroducedTypeSource> sources = new List<HotReloadIntroducedTypeSource>();
            for (int index = 0; index < descriptors.Count; index++)
            {
                sources.Add(new HotReloadIntroducedTypeSource(paths.CreateSourcePath(index), descriptors[index]));
            }

            return new HotReloadIntroducedTypeCompilationRequest(
                sources,
                paths.DllPath,
                paths.PdbPath,
                paths.AssemblyFullName,
                descriptors,
                referencePaths,
                defineSymbols);
        }

        private static IReadOnlyList<HotReloadIntroducedTypeSource> CopySources(
            IReadOnlyList<HotReloadIntroducedTypeSource> sources)
        {
            if (sources == null || sources.Count == 0)
            {
                throw new ArgumentException("Introduced-type sources must not be empty.", nameof(sources));
            }

            HashSet<string> paths = new HashSet<string>(StringComparer.Ordinal);
            List<HotReloadIntroducedTypeSource> copiedSources =
                new List<HotReloadIntroducedTypeSource>(sources.Count);
            foreach (HotReloadIntroducedTypeSource source in sources)
            {
                if (source == null)
                {
                    throw new ArgumentException("Introduced-type sources must not contain null.", nameof(sources));
                }

                string normalizedPath = Path.GetFullPath(source.Path);
                if (!paths.Add(normalizedPath))
                {
                    throw new ArgumentException("Introduced-type source paths must be unique.", nameof(sources));
                }

                copiedSources.Add(new HotReloadIntroducedTypeSource(normalizedPath, source.Descriptor));
            }

            return copiedSources.AsReadOnly();
        }

        private static IReadOnlyList<HotReloadIntroducedTypeDescriptor> CopyDescriptors(
            IReadOnlyList<HotReloadIntroducedTypeDescriptor> descriptors)
        {
            if (descriptors == null || descriptors.Count == 0)
            {
                throw new ArgumentException("Introduced-type descriptors must not be empty.", nameof(descriptors));
            }

            List<HotReloadIntroducedTypeDescriptor> copiedDescriptors =
                new List<HotReloadIntroducedTypeDescriptor>(descriptors.Count);
            foreach (HotReloadIntroducedTypeDescriptor descriptor in descriptors)
            {
                if (descriptor == null)
                {
                    throw new ArgumentException("Introduced-type descriptors must not contain null.", nameof(descriptors));
                }

                copiedDescriptors.Add(descriptor);
            }

            return copiedDescriptors.AsReadOnly();
        }

        private static IReadOnlyList<string> CopyStrings(IReadOnlyList<string> values)
        {
            return values == null
                ? Array.Empty<string>()
                : new List<string>(values).AsReadOnly();
        }
    }

    /// <summary>
    /// Runs introduced-type compilation through the Unity Roslyn backend without fallback.
    /// </summary>
    internal sealed class HotReloadIntroducedTypeCompilerEnvironment : IHotReloadIntroducedTypeCompilerEnvironment
    {
        public async Task<ExternalCompilerPaths> ResolveCompilerPathsOnMainThreadAsync(CancellationToken ct)
        {
            await MainThreadSwitcher.SwitchToMainThread(ct);
            return ExternalCompilerPathResolver.Resolve();
        }

        public Task<DynamicCompilationBackendResult> CompileAsync(
            HotReloadIntroducedTypeCompilationRequest request,
            ExternalCompilerPaths paths,
            CancellationToken ct)
        {
            RoslynCompilerOptions options = new RoslynCompilerOptions(
                request.DefineSymbols,
                allowUnsafeCode: false,
                emitDebugCode: true);
            List<string> sourcePaths = new List<string>();
            foreach (HotReloadIntroducedTypeSource source in request.Sources)
            {
                sourcePaths.Add(source.Path);
            }

            return RoslynCompilerBackend.CompileMultipleSourcesAsync(
                sourcePaths,
                request.DllPath,
                new List<string>(request.ReferencePaths),
                paths,
                options,
                ct,
                markBuildStarted: static () => { },
                markBuildFinished: static () => { },
                incrementBuildCount: static () => { });
        }

        public bool FileExists(string path)
        {
            return File.Exists(path);
        }

        public AssemblyName ReadAssemblyName(string path)
        {
            return AssemblyName.GetAssemblyName(path);
        }

        public byte[] ReadAllBytes(string path)
        {
            return File.ReadAllBytes(path);
        }

        public CompiledAssemblyLoadResult Load(byte[] assemblyBytes, byte[] pdbBytes)
        {
            return CompiledAssemblyLoader.Load(assemblyBytes, pdbBytes);
        }

        public IReadOnlyCollection<string> ReadDefinedTypeNames(string path)
        {
            using AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(path);
            List<string> typeNames = new List<string>();
            foreach (TypeDefinition type in assembly.MainModule.Types)
            {
                if (type.Name != "<Module>")
                {
                    typeNames.Add(type.FullName.Replace('/', '.'));
                }
            }

            return typeNames;
        }

        public void WriteSource(string path, string source)
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, source);
        }
    }

    /// <summary>
    /// Reports a prepared artifact or a rejection that left both prepared and active state unchanged.
    /// </summary>
    internal sealed class HotReloadIntroducedTypeSource
    {
        public string Path { get; }

        public string Text { get; }

        public HotReloadIntroducedTypeDescriptor Descriptor { get; }

        public HotReloadIntroducedTypeSource(string path, HotReloadIntroducedTypeDescriptor descriptor)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Source path must not be empty.", nameof(path));
            }

            Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
            if (string.IsNullOrWhiteSpace(Descriptor.Source))
            {
                throw new ArgumentException("Source text must not be empty.", nameof(descriptor));
            }

            Path = path;
            Text = Descriptor.Source;
        }
    }

    internal sealed class HotReloadIntroducedTypeCompilerDiagnostic
    {
        public string OwnerProjectRelativePath { get; }

        public string Message { get; }

        public HotReloadIntroducedTypeCompilerDiagnostic(string ownerProjectRelativePath, string message)
        {
            OwnerProjectRelativePath = ownerProjectRelativePath ?? string.Empty;
            Message = message ?? string.Empty;
        }
    }

    internal sealed class HotReloadIntroducedTypeCompilerResult
    {
        public bool Success { get; }

        public HotReloadIntroducedTypeArtifact Artifact { get; }

        public string ErrorMessage { get; }

        public IReadOnlyList<HotReloadIntroducedTypeCompilerDiagnostic> Diagnostics { get; }

        private HotReloadIntroducedTypeCompilerResult(
            bool success,
            HotReloadIntroducedTypeArtifact artifact,
            string errorMessage,
            IReadOnlyList<HotReloadIntroducedTypeCompilerDiagnostic> diagnostics)
        {
            Success = success;
            Artifact = artifact;
            ErrorMessage = errorMessage;
            Diagnostics = diagnostics ?? Array.Empty<HotReloadIntroducedTypeCompilerDiagnostic>();
        }

        public static HotReloadIntroducedTypeCompilerResult Prepared(HotReloadIntroducedTypeArtifact artifact)
        {
            if (artifact == null)
            {
                throw new ArgumentNullException(nameof(artifact));
            }

            return new HotReloadIntroducedTypeCompilerResult(
                true,
                artifact,
                string.Empty,
                Array.Empty<HotReloadIntroducedTypeCompilerDiagnostic>());
        }

        public static HotReloadIntroducedTypeCompilerResult Failure(
            string errorMessage,
            IReadOnlyList<HotReloadIntroducedTypeCompilerDiagnostic> diagnostics = null)
        {
            if (string.IsNullOrWhiteSpace(errorMessage))
            {
                throw new ArgumentException("Failure message must not be empty.", nameof(errorMessage));
            }

            return new HotReloadIntroducedTypeCompilerResult(false, null, errorMessage, diagnostics);
        }
    }
}
