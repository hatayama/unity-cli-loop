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
            CancellationToken ct)
        {
            Debug.Assert(!string.IsNullOrEmpty(shimSource), "shimSource must not be empty.");
            Debug.Assert(referencePaths != null, "referencePaths must not be null.");
            Debug.Assert(defineSymbols != null, "defineSymbols must not be null.");

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

                RoslynCompilerOptions compilerOptions = new RoslynCompilerOptions(defineSymbols, allowUnsafeCode: false);
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

                List<string> errors = CollectErrors(backendResult.CompilerMessages);
                if (errors.Count > 0)
                {
                    return HotReloadShimCompileResult.Failure(ComposeShimCompileFailureMessage(errors));
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

                return HotReloadShimCompileResult.SuccessResult(loadResult.CompiledAssembly);
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
        // errors) misdirects the caller.
        private static readonly string[] MissingMemberDiagnosticCodes =
        {
            "CS0103",
            "CS0117",
            "CS0246",
            "CS1061"
        };

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
                    if (error.Contains(MissingMemberDiagnosticCodes[codeIndex]))
                    {
                        return message + "\n" + HotReloadConstants.NewMemberCompileHint;
                    }
                }
            }

            return message;
        }

        private static List<string> CollectErrors(CompilerMessage[] compilerMessages)
        {
            List<string> errors = new List<string>();
            if (compilerMessages == null)
            {
                return errors;
            }

            foreach (CompilerMessage compilerMessage in compilerMessages)
            {
                if (compilerMessage.type == CompilerMessageType.Error)
                {
                    errors.Add(compilerMessage.message);
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
    /// Outcome of compiling and loading a hot-reload shim assembly.
    /// </summary>
    internal sealed class HotReloadShimCompileResult
    {
        public bool Success { get; }
        public Assembly Assembly { get; }
        public string ErrorMessage { get; }

        private HotReloadShimCompileResult(bool success, Assembly assembly, string errorMessage)
        {
            Success = success;
            Assembly = assembly;
            ErrorMessage = errorMessage;
        }

        public static HotReloadShimCompileResult SuccessResult(Assembly assembly)
        {
            return new HotReloadShimCompileResult(true, assembly, string.Empty);
        }

        public static HotReloadShimCompileResult Failure(string errorMessage)
        {
            return new HotReloadShimCompileResult(false, null, errorMessage);
        }
    }
}
