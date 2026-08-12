using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using UnityEditor.Compilation;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.ToolContracts;

using Assembly = System.Reflection.Assembly;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Compiles worker-emitted shim source against publicized ScriptAssemblies references via
    /// <see cref="RoslynCompilerBackend"/> (not <c>IDynamicCompilationService</c> — that path
    /// cannot inject publicized copies or caller-supplied defines).
    /// </summary>
    internal static class HotReloadShimCompiler
    {
        /// <summary>
        /// Compiles <paramref name="shimSource"/> and loads the resulting assembly into the
        /// Editor domain. The original (non-publicized) target assembly must not appear in
        /// <paramref name="referencePaths"/>.
        /// </summary>
        public static async Task<HotReloadShimCompileResult> CompileAndLoadAsync(
            string shimSource,
            IReadOnlyList<string> referencePaths,
            IReadOnlyList<string> defineSymbols,
            string projectRelativePath,
            CancellationToken ct)
        {
            Debug.Assert(!string.IsNullOrEmpty(shimSource), "shimSource must not be empty.");
            Debug.Assert(referencePaths != null, "referencePaths must not be null.");
            Debug.Assert(defineSymbols != null, "defineSymbols must not be null.");
            Debug.Assert(projectRelativePath != null, "projectRelativePath must not be null.");

            // Resolver and Application.dataPath (CreateWorkDirectory) need the Unity main thread.
            await MainThreadSwitcher.SwitchToMainThread(ct);

            ExternalCompilerPaths externalCompilerPaths = ExternalCompilerPathResolver.Resolve();
            if (externalCompilerPaths == null)
            {
                return HotReloadShimCompileResult.Failure(
                    "External compiler paths could not be resolved for this Unity installation.");
            }

            string workDirectory = CreateWorkDirectory();
            try
            {
                string sourcePath = Path.Combine(workDirectory, "HotReloadShim.cs");
                string dllPath = Path.Combine(workDirectory, "HotReloadShim.dll");
                string pdbPath = Path.ChangeExtension(dllPath, ".pdb");
                File.WriteAllText(sourcePath, shimSource);

                List<string> references = new List<string>(referencePaths.Count);
                foreach (string referencePath in referencePaths)
                {
                    references.Add(referencePath);
                }

                // Why emitDebugCode: Release optimization drops interface-typed locals from PDB
                // scopes, so pause-point CapturedVariables miss them after a hot-reload patch.
                RoslynCompilerOptions compilerOptions = new RoslynCompilerOptions(
                    defineSymbols,
                    allowUnsafeCode: false,
                    emitDebugCode: true);
                DynamicCompilationBackendResult backendResult = await RoslynCompilerBackend.CompileAsync(
                    sourcePath,
                    dllPath,
                    references,
                    externalCompilerPaths,
                    compilerOptions,
                    ct,
                    markBuildStarted: static () => { },
                    markBuildFinished: static () => { },
                    incrementBuildCount: static () => { }).ConfigureAwait(false);

                List<HotReloadShimCompileError> errors = CollectErrors(backendResult.CompilerMessages);
                if (errors.Count > 0)
                {
                    List<string> errorMessages = new List<string>(errors.Count);
                    foreach (HotReloadShimCompileError error in errors)
                    {
                        // Why only user-file mapped lines: scaffold diagnostics (temp HotReloadShim.cs)
                        // must not grow a fake "(line N)" that looks like the edited source.
                        errorMessages.Add(FormatErrorMessageWithMappedLine(error, projectRelativePath));
                    }

                    return HotReloadShimCompileResult.Failure(
                        ComposeShimCompileFailureMessage(errorMessages), errors);
                }

                if (!File.Exists(dllPath))
                {
                    return HotReloadShimCompileResult.Failure("Shim dll was not produced: " + dllPath);
                }

                byte[] assemblyBytes = File.ReadAllBytes(dllPath);
                byte[] pdbBytes = File.Exists(pdbPath) ? File.ReadAllBytes(pdbPath) : null;
                CompiledAssemblyLoadResult loadResult = CompiledAssemblyLoader.Load(assemblyBytes, pdbBytes);
                if (!loadResult.Success || loadResult.CompiledAssembly == null)
                {
                    return HotReloadShimCompileResult.Failure(
                        "Shim assembly compiled but failed to load: " + dllPath);
                }

                return HotReloadShimCompileResult.SuccessResult(
                    loadResult.CompiledAssembly,
                    assemblyBytes,
                    pdbBytes);
            }
            finally
            {
                if (Directory.Exists(workDirectory))
                {
                    Directory.Delete(workDirectory, recursive: true);
                }
            }
        }

        // Only these diagnostics indicate a member/type the compiled assembly does not have yet;
        // appending the "run a real compile" hint to unrelated errors (CS0229 ambiguity, syntax
        // errors) misdirects the caller. CS1501/CS7036 cover calls whose target signature changed
        // in the edit but not yet in the compiled assembly.
        private static readonly string[] MissingMemberDiagnosticCodes =
        {
            "CS0103",
            "CS0117",
            "CS0246",
            "CS1061",
            "CS1501",
            "CS7036"
        };

        /// <summary>
        /// Appends " (line N)" only when the diagnostic's #line-mapped file refers to the user's
        /// project-relative path. Scaffold-path errors keep the bare message.
        /// </summary>
        private static string FormatErrorMessageWithMappedLine(
            HotReloadShimCompileError error,
            string projectRelativePath)
        {
            if (error.Line > 0
                && !string.IsNullOrEmpty(error.File)
                && HotReloadSourcePathNormalizer.PathsReferToSameFile(error.File, projectRelativePath))
            {
                return error.Message + " (line " + error.Line + ")";
            }

            return error.Message;
        }

        internal static string ComposeShimCompileFailureMessage(IReadOnlyList<string> errors)
        {
            Debug.Assert(errors != null, "errors must not be null.");
            Debug.Assert(errors.Count > 0, "errors must not be empty.");

            string message = string.Join("\n", errors);
            for (int index = 0; index < errors.Count; index++)
            {
                string error = errors[index];
                for (int codeIndex = 0; codeIndex < MissingMemberDiagnosticCodes.Length; codeIndex++)
                {
                    // Match the diagnostic-code prefix ("CS0103: …"), not a bare Contains —
                    // otherwise a message that merely mentions the code text could append the hint.
                    if (error.StartsWith(MissingMemberDiagnosticCodes[codeIndex] + ":", StringComparison.Ordinal))
                    {
                        return message + "\n" + HotReloadConstants.NewMemberCompileHint;
                    }
                }
            }

            return message;
        }

        private static List<HotReloadShimCompileError> CollectErrors(CompilerMessage[] compilerMessages)
        {
            List<HotReloadShimCompileError> errors = new List<HotReloadShimCompileError>();
            if (compilerMessages == null)
            {
                return errors;
            }

            foreach (CompilerMessage compilerMessage in compilerMessages)
            {
                if (compilerMessage.type == CompilerMessageType.Error)
                {
                    // Why keep file: #line-mapped diagnostics point at the user's project-relative
                    // path (or an absolute form of it); attribution matches via suffix-tolerant
                    // path compare, not exact equality across the three compile backends.
                    errors.Add(
                        new HotReloadShimCompileError(
                            compilerMessage.file ?? string.Empty,
                            compilerMessage.line,
                            compilerMessage.message));
                }
            }

            return errors;
        }

        private static string CreateWorkDirectory()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string workDirectory = Path.Combine(
                projectRoot,
                "Library",
                "UloopHotReload",
                "ShimCompile",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(workDirectory);
            return workDirectory;
        }
    }

    /// <summary>
    /// One compiler error from a shim compile. Line/File are #line-mapped when directives are
    /// present (original user file + 1-based original line); 0 / empty when the diagnostic
    /// carried no location.
    /// </summary>
    internal sealed class HotReloadShimCompileError
    {
        public string File { get; }
        public int Line { get; }
        public string Message { get; }

        public HotReloadShimCompileError(string file, int line, string message)
        {
            File = file ?? string.Empty;
            Line = line;
            Message = message;
        }
    }

    /// <summary>
    /// Outcome of compiling and loading a hot-reload shim assembly. Successful results keep the
    /// dll/pdb bytes so pause-point can resolve sequence points against the shim PDB after the
    /// compile work directory is deleted.
    /// </summary>
    internal sealed class HotReloadShimCompileResult
    {
        private static readonly IReadOnlyList<HotReloadShimCompileError> EmptyErrors =
            Array.Empty<HotReloadShimCompileError>();

        public bool Success { get; }
        public Assembly Assembly { get; }
        public byte[] AssemblyBytes { get; }
        public byte[] PdbBytes { get; }
        public string ErrorMessage { get; }
        public IReadOnlyList<HotReloadShimCompileError> Errors { get; }

        private HotReloadShimCompileResult(
            bool success,
            Assembly assembly,
            string errorMessage,
            IReadOnlyList<HotReloadShimCompileError> errors,
            byte[] assemblyBytes,
            byte[] pdbBytes)
        {
            Success = success;
            Assembly = assembly;
            ErrorMessage = errorMessage;
            Errors = errors;
            AssemblyBytes = assemblyBytes;
            PdbBytes = pdbBytes;
        }

        public static HotReloadShimCompileResult SuccessResult(
            Assembly assembly,
            byte[] assemblyBytes,
            byte[] pdbBytes)
        {
            return new HotReloadShimCompileResult(
                true,
                assembly,
                string.Empty,
                EmptyErrors,
                assemblyBytes,
                pdbBytes);
        }

        // errors stays empty for failures that never reached the compiler (e.g. missing external
        // compiler paths, a dll that failed to load) — only an actual compile failure has any.
        public static HotReloadShimCompileResult Failure(
            string errorMessage, IReadOnlyList<HotReloadShimCompileError> errors = null)
        {
            return new HotReloadShimCompileResult(
                false,
                null,
                errorMessage,
                errors ?? EmptyErrors,
                null,
                null);
        }
    }
}
