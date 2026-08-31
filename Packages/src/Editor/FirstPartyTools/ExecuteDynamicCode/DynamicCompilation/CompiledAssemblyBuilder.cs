using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor.Compilation;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Builds Compiled Assembly data from the inputs provided by this module.
    /// </summary>
    internal sealed class CompiledAssemblyBuilder : ICompiledAssemblyBuilder
    {
        private static int _compileCounter;
        // Hoisting turns constant literals into variables, which loses implicit narrowing
        // conversions (e.g. an int literal argument no longer converts to a byte parameter
        // such as Color32's), so CS1503 needs the same non-hoisted recompile fallback as the
        // other hoisting-caused error codes below.
        private static readonly string[] LiteralHoistingFallbackErrorCodes =
            { "CS0133", "CS0150", "CS0182", "CS1736", "CS1503" };

        private readonly ExternalCompilerPathResolutionService _externalCompilerPathResolver;
        private readonly DynamicReferenceSetBuilderService _referenceSetBuilder;
        private readonly DynamicCompilationBackend _compilationBackend;

        /// <summary>
        /// Carries the result data produced by Build Attempt behavior.
        /// </summary>
        private sealed class BuildAttemptResult
        {
            public string UpdatedSource { get; }

            public CompilerDiagnostics Diagnostics { get; }

            public Dictionary<string, List<string>> AmbiguousTypeCandidates { get; }

            public List<AutoInjectedNamespace> AutoInjectedNamespaces { get; }

            public byte[] AssemblyBytes { get; }

            public byte[] PdbBytes { get; }

            public BuildAttemptResult(
                string updatedSource,
                CompilerDiagnostics diagnostics,
                Dictionary<string, List<string>> ambiguousTypeCandidates,
                List<AutoInjectedNamespace> autoInjectedNamespaces,
                byte[] assemblyBytes,
                byte[] pdbBytes)
            {
                UpdatedSource = updatedSource;
                Diagnostics = diagnostics;
                AmbiguousTypeCandidates = ambiguousTypeCandidates;
                AutoInjectedNamespaces = autoInjectedNamespaces;
                AssemblyBytes = assemblyBytes;
                PdbBytes = pdbBytes;
            }
        }

        public CompiledAssemblyBuilder(
            ExternalCompilerPathResolutionService externalCompilerPathResolver,
            DynamicReferenceSetBuilderService referenceSetBuilder,
            DynamicCompilationBackend compilationBackend)
        {
            _externalCompilerPathResolver = externalCompilerPathResolver;
            _referenceSetBuilder = referenceSetBuilder;
            _compilationBackend = compilationBackend;
        }

        public async Task<CompiledAssemblyBuildResult> BuildAsync(
            DynamicCompilationPlan plan,
            RoslynCompilerOptions compilerOptions,
            CancellationToken ct = default)
        {
            Debug.Assert(plan != null, "plan must not be null");
            Debug.Assert(compilerOptions != null, "compilerOptions must not be null");

            ct.ThrowIfCancellationRequested();

            ExternalCompilerPaths externalCompilerPaths = _externalCompilerPathResolver.Resolve();
            string tempDirectoryPath = Path.Combine("Temp", "UnityCliLoopCompilation");
            int compileCounter = Interlocked.Increment(ref _compileCounter);
            string uniqueName = CreateUniqueCompilationName(plan.ClassName, compileCounter);
            string sourcePath = Path.Combine(tempDirectoryPath, $"{uniqueName}.cs");
            string dllPath = Path.Combine(tempDirectoryPath, $"{uniqueName}.dll");
            bool canDeleteTempFiles = true;
            double referenceResolutionMilliseconds = 0;
            double buildMilliseconds = 0;
            int buildCount = 0;
            DynamicCompilationBackendKind compilationBackendKind = DynamicCompilationBackendKind.Unknown;

            Directory.CreateDirectory(tempDirectoryPath);

            try
            {
                async Task<CompilerMessage[]> BuildFunc(
                    string resolvedSourcePath,
                    string resolvedDllPath,
                    List<string> resolvedReferences,
                    CancellationToken cancellationToken)
                {
                    Stopwatch buildStopwatch = Stopwatch.StartNew();
                    DynamicCompilationBackendResult backendResult = await _compilationBackend.CompileAsync(
                        resolvedSourcePath,
                        resolvedDllPath,
                        resolvedReferences,
                        externalCompilerPaths,
                        compilerOptions,
                        cancellationToken,
                        () => canDeleteTempFiles = false,
                        () => canDeleteTempFiles = true,
                        () => buildCount++).ConfigureAwait(false);
                    compilationBackendKind = backendResult.BackendKind;
                    buildStopwatch.Stop();
                    buildMilliseconds += buildStopwatch.Elapsed.TotalMilliseconds;
                    return backendResult.CompilerMessages;
                }

                BuildAttemptResult attemptResult = await BuildPreparedCodeAsync(plan.PreparedCode, ct).ConfigureAwait(false);
                bool shouldCacheResult = true;

                if (ShouldRetryWithoutLiteralHoisting(plan.PreparedCode, attemptResult.Diagnostics))
                {
                    PreparedDynamicCode fallbackPreparedCode = DynamicCodeSourcePreparer.PrepareWithoutLiteralHoisting(
                        plan.OriginalRequest.Code,
                        plan.NamespaceName,
                        plan.ClassName);
                    attemptResult = await BuildPreparedCodeAsync(fallbackPreparedCode, ct).ConfigureAwait(false);
                    shouldCacheResult = false;
                }

                return new CompiledAssemblyBuildResult(
                    attemptResult.UpdatedSource,
                    attemptResult.Diagnostics,
                    attemptResult.AmbiguousTypeCandidates,
                    attemptResult.AutoInjectedNamespaces,
                    attemptResult.AssemblyBytes,
                    attemptResult.PdbBytes,
                    referenceResolutionMilliseconds,
                    buildMilliseconds,
                    buildCount,
                    shouldCacheResult,
                    compilationBackendKind);

                async Task<BuildAttemptResult> BuildPreparedCodeAsync(
                    PreparedDynamicCode preparedCode,
                    CancellationToken cancellationToken)
                {
                    string wrappedCode = preparedCode.PreparedSource;
                    string originalWrappedCode = wrappedCode;
                    bool preUsingAdded = false;
                    PreUsingResult preUsingResult = null;

                    Stopwatch initialReferenceResolutionStopwatch = Stopwatch.StartNew();
                    List<string> initialReferences = BuildInitialReferences(
                        plan,
                        externalCompilerPaths,
                        preparedCode,
                        ref wrappedCode,
                        ref preUsingAdded,
                        ref preUsingResult);
                    initialReferenceResolutionStopwatch.Stop();
                    referenceResolutionMilliseconds += initialReferenceResolutionStopwatch.Elapsed.TotalMilliseconds;

                    AutoUsingResolver resolver = new();
                    AutoUsingResult autoResult = await resolver.ResolveAsync(
                        sourcePath,
                        dllPath,
                        wrappedCode,
                        initialReferences,
                        BuildFunc,
                        cancellationToken).ConfigureAwait(false);
                    referenceResolutionMilliseconds += autoResult.ReferenceResolutionMilliseconds;

                    wrappedCode = autoResult.UpdatedSource;
                    CompilerDiagnostics diagnostics = CompilerDiagnostics.FromMessages(autoResult.Messages);

                    bool preUsingRolledBack = false;
                    if (diagnostics.Errors.Count > 0 && preUsingAdded && diagnostics.HasAmbiguityErrors)
                    {
                        Stopwatch rollbackReferenceResolutionStopwatch = Stopwatch.StartNew();
                        List<string> rollbackReferences = _referenceSetBuilder.BuildReferenceSet(
                            plan.OriginalRequest.AdditionalReferences,
                            null,
                            externalCompilerPaths);
                        rollbackReferenceResolutionStopwatch.Stop();
                        referenceResolutionMilliseconds += rollbackReferenceResolutionStopwatch.Elapsed.TotalMilliseconds;

                        AutoUsingResult rollbackResult = await resolver.ResolveAsync(
                            sourcePath,
                            dllPath,
                            originalWrappedCode,
                            rollbackReferences,
                            BuildFunc,
                            cancellationToken).ConfigureAwait(false);
                        referenceResolutionMilliseconds += rollbackResult.ReferenceResolutionMilliseconds;

                        CompilerDiagnostics rollbackDiagnostics = CompilerDiagnostics.FromMessages(rollbackResult.Messages);
                        if (rollbackDiagnostics.Errors.Count < diagnostics.Errors.Count)
                        {
                            wrappedCode = rollbackResult.UpdatedSource;
                            diagnostics = rollbackDiagnostics;
                            autoResult = rollbackResult;
                            preUsingRolledBack = true;
                        }
                    }

                    List<AutoInjectedNamespace> autoInjectedNamespaces = MergeAutoInjectedNamespaces(
                        preUsingRolledBack,
                        preUsingResult,
                        autoResult);

                    byte[] assemblyBytes = null;
                    byte[] pdbBytes = null;
                    if (diagnostics.Errors.Count == 0)
                    {
                        assemblyBytes = File.ReadAllBytes(dllPath);
                        // Why read before delete: portable PDB is required for Assembly.Load to
                        // attach sequence points; the temp file is deleted with the dll below.
                        string pdbPath = Path.ChangeExtension(dllPath, ".pdb");
                        if (File.Exists(pdbPath))
                        {
                            pdbBytes = File.ReadAllBytes(pdbPath);
                        }
                    }

                    return new BuildAttemptResult(
                        wrappedCode,
                        diagnostics,
                        autoResult.AmbiguousTypeCandidates,
                        autoInjectedNamespaces,
                        assemblyBytes,
                        pdbBytes);
                }
            }
            finally
            {
                if (canDeleteTempFiles)
                {
                    File.Delete(sourcePath);
                    File.Delete(dllPath);
                    File.Delete(Path.ChangeExtension(dllPath, ".pdb"));
                }
            }
        }

        internal static string CreateUniqueCompilationName(string className, int compileCounter)
        {
            string safeClassName = SanitizeCompilationFileNameSegment(className);
            return $"{safeClassName}_{compileCounter}";
        }

        internal static string SanitizeCompilationFileNameSegment(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return DynamicCodeConstants.DEFAULT_CLASS_NAME;
            }

            StringBuilder builder = new(value.Length);
            foreach (char ch in value)
            {
                builder.Append(IsUnsafeFileNameCharacter(ch) ? '_' : ch);
            }

            string sanitized = builder.ToString().Trim('.');
            if (string.IsNullOrWhiteSpace(sanitized))
            {
                return DynamicCodeConstants.DEFAULT_CLASS_NAME;
            }

            return sanitized;
        }

        private static bool IsUnsafeFileNameCharacter(char value)
        {
            if (value == Path.DirectorySeparatorChar || value == Path.AltDirectorySeparatorChar)
            {
                return true;
            }

            switch (value)
            {
                case '<':
                case '>':
                case ':':
                case '"':
                case '/':
                case '\\':
                case '|':
                case '?':
                case '*':
                    return true;
            }

            foreach (char invalidChar in Path.GetInvalidFileNameChars())
            {
                if (value == invalidChar)
                {
                    return true;
                }
            }

            return false;
        }

        private List<string> BuildInitialReferences(
            DynamicCompilationPlan plan,
            ExternalCompilerPaths externalCompilerPaths,
            PreparedDynamicCode preparedCode,
            ref string wrappedCode,
            ref bool preUsingAdded,
            ref PreUsingResult preUsingResult)
        {
            if (!preparedCode.IsScriptMode)
            {
                return _referenceSetBuilder.BuildReferenceSet(
                    plan.OriginalRequest.AdditionalReferences,
                    null,
                    externalCompilerPaths);
            }

            preUsingResult = PreUsingResolver.Resolve(wrappedCode, AssemblyTypeIndex.Instance);
            preUsingAdded = !ReferenceEquals(preUsingResult.UpdatedSource, wrappedCode);
            wrappedCode = preUsingResult.UpdatedSource;
            return _referenceSetBuilder.BuildReferenceSet(
                plan.OriginalRequest.AdditionalReferences,
                preUsingResult.AddedAssemblyReferences,
                externalCompilerPaths);
        }

        internal static bool ShouldRetryWithoutLiteralHoisting(
            PreparedDynamicCode preparedCode,
            CompilerDiagnostics diagnostics)
        {
            if (preparedCode == null || diagnostics == null)
            {
                return false;
            }

            if (!preparedCode.IsScriptMode || preparedCode.HoistedLiteralBindings.Count == 0)
            {
                return false;
            }

            foreach (CompilationError error in diagnostics.Errors)
            {
                foreach (string errorCode in LiteralHoistingFallbackErrorCodes)
                {
                    if (error.ErrorCode == errorCode)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        internal static List<AutoInjectedNamespace> MergeAutoInjectedNamespaces(
            bool preUsingRolledBack,
            PreUsingResult preUsingResult,
            AutoUsingResult autoResult)
        {
            List<AutoInjectedNamespace> mergedNamespaces = new();
            HashSet<string> seenNamespaces = new(System.StringComparer.Ordinal);

            if (!preUsingRolledBack && preUsingResult != null)
            {
                AppendUniqueAttributions(
                    mergedNamespaces,
                    seenNamespaces,
                    preUsingResult.AddedNamespaceAttributions);
            }

            AppendUniqueAttributions(
                mergedNamespaces,
                seenNamespaces,
                autoResult.AddedNamespaceAttributions);
            return mergedNamespaces;
        }

        private static void AppendUniqueAttributions(
            List<AutoInjectedNamespace> mergedNamespaces,
            HashSet<string> seenNamespaces,
            IReadOnlyList<AutoInjectedNamespace> attributions)
        {
            if (attributions == null)
            {
                return;
            }

            foreach (AutoInjectedNamespace attribution in attributions)
            {
                if (seenNamespaces.Add(attribution.Namespace))
                {
                    mergedNamespaces.Add(attribution);
                }
            }
        }
    }
}
