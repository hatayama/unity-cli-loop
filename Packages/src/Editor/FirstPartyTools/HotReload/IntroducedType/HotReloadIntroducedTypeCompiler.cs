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
        private readonly IHotReloadRoslynCompilerEnvironment environment;
        private readonly HotReloadRoslynCompiler compiler;

        public HotReloadIntroducedTypeCompiler(IHotReloadRoslynCompilerEnvironment environment)
        {
            this.environment = environment ?? throw new ArgumentNullException(nameof(environment));
            compiler = new HotReloadRoslynCompiler(environment);
        }

        public async Task<HotReloadIntroducedTypeCompilerResult> CompileAsync(
            HotReloadIntroducedTypeCompilationRequest request,
            CancellationToken ct)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            HotReloadRoslynCompileOutcome outcome = await compiler
                .CompileAsync(CreateCompileRequest(request), ct)
                .ConfigureAwait(false);
            if (!outcome.PathsResolved)
            {
                return HotReloadIntroducedTypeCompilerResult.Failure(
                    HotReloadConstants.CompilerPathsUnresolvedMessage);
            }

            HotReloadIntroducedTypeCompilerResult backendFailure =
                ValidateBackendResult(request, outcome.BackendResult);
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

        // Why the fallback is refused: the artifact stays on disk and later compilations reference
        // it by path, which an AssemblyBuilder-built assembly cannot satisfy.
        private static HotReloadRoslynCompileRequest CreateCompileRequest(
            HotReloadIntroducedTypeCompilationRequest request)
        {
            List<HotReloadRoslynCompileSource> sources =
                new List<HotReloadRoslynCompileSource>(request.Sources.Count);
            foreach (HotReloadIntroducedTypeSource source in request.Sources)
            {
                sources.Add(new HotReloadRoslynCompileSource(source.Path, source.Text));
            }

            return new HotReloadRoslynCompileRequest(
                sources,
                request.DllPath,
                request.ReferencePaths,
                request.DefineSymbols,
                allowAssemblyBuilderFallback: false);
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
                if (!emittedTypeNames.Contains(descriptor.MetadataName.Value))
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

                // Why line and column travel separately: the parsed message keeps only the
                // "CSxxxx: <text>" part, so dropping them here would leave the response without
                // the position the reader needs to find the offending source line.
                diagnostics.Add(new HotReloadIntroducedTypeCompilerDiagnostic(
                    ownerProjectRelativePath,
                    message.message,
                    message.line,
                    message.column));
            }

            return diagnostics;
        }
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
            IReadOnlyList<HotReloadIntroducedTypeSource> copiedSources = CopySources(sources);
            IReadOnlyList<HotReloadIntroducedTypeDescriptor> copiedDescriptors = CopyDescriptors(descriptors);
            ValidateSourceDescriptorPairing(copiedSources, copiedDescriptors);
            Sources = copiedSources;
            DllPath = dllPath;
            PdbPath = pdbPath;
            ExpectedAssemblyFullName = expectedAssemblyFullName;
            Descriptors = copiedDescriptors;
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

        // The compiler writes one source file per entry and then loads the produced assembly, so a
        // count mismatch, a source paired with a different descriptor, or two descriptors sharing
        // one identity has to be rejected here. Detecting it after the load would leave an
        // unloadable assembly behind for an activation that can only be refused.
        private static void ValidateSourceDescriptorPairing(
            IReadOnlyList<HotReloadIntroducedTypeSource> sources,
            IReadOnlyList<HotReloadIntroducedTypeDescriptor> descriptors)
        {
            if (sources.Count != descriptors.Count)
            {
                throw new ArgumentException(
                    "Introduced-type sources and descriptors must have the same count.",
                    nameof(sources));
            }

            HashSet<string> identities = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < sources.Count; index++)
            {
                if (!ReferenceEquals(sources[index].Descriptor, descriptors[index]))
                {
                    throw new ArgumentException(
                        "Each introduced-type source must carry the descriptor at the same position.",
                        nameof(sources));
                }

                if (!identities.Add(descriptors[index].BuildIdentity()))
                {
                    throw new ArgumentException(
                        "Introduced-type descriptors must have unique identities.",
                        nameof(descriptors));
                }
            }
        }

        private static IReadOnlyList<HotReloadIntroducedTypeSource> CopySources(
            IReadOnlyList<HotReloadIntroducedTypeSource> sources)
        {
            if (sources == null || sources.Count == 0)
            {
                throw new ArgumentException("Introduced-type sources must not be empty.", nameof(sources));
            }

            // Every introduced-type source path is generated with a distinct ordinal suffix, so two
            // paths differing only in case are never legitimate here. Rejecting them regardless of
            // platform matters because a case-insensitive volume (the macOS default, and Windows)
            // resolves both spellings to one file, and the second write would silently replace the
            // first descriptor's source before compilation.
            HashSet<string> paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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

        /// <summary>The one-based source line of the diagnostic, or zero when it carries none.</summary>
        public int Line { get; }

        /// <summary>The one-based source column of the diagnostic, or zero when it carries none.</summary>
        public int Column { get; }

        public HotReloadIntroducedTypeCompilerDiagnostic(
            string ownerProjectRelativePath,
            string message,
            int line,
            int column)
        {
            OwnerProjectRelativePath = ownerProjectRelativePath ?? string.Empty;
            Message = message ?? string.Empty;
            Line = line;
            Column = column;
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
