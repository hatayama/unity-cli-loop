using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using Mono.Cecil;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Runs hot-reload Roslyn compilation against the real Unity installation and the real file
    /// system.
    /// </summary>
    internal sealed class HotReloadRoslynCompilerEnvironment : IHotReloadRoslynCompilerEnvironment
    {
        public async Task<ExternalCompilerPaths> ResolveCompilerPathsOnMainThreadAsync(CancellationToken ct)
        {
            await MainThreadSwitcher.SwitchToMainThread(ct);
            return ExternalCompilerPathResolver.Resolve();
        }

        public Task<DynamicCompilationBackendResult> CompileAsync(
            HotReloadRoslynCompileRequest request,
            ExternalCompilerPaths paths,
            CancellationToken ct)
        {
            // Why emitDebugCode: Release optimization drops interface-typed locals from PDB
            // scopes, so pause-point CapturedVariables miss them after a hot-reload patch.
            RoslynCompilerOptions options = new RoslynCompilerOptions(
                request.DefineSymbols,
                allowUnsafeCode: false,
                emitDebugCode: true);
            List<string> references = new List<string>(request.ReferencePaths);

            // Why two backend entry points rather than one plus a flag: the fallback is selected
            // inside the backend by which entry point is called, and the single-source one is the
            // only one that allows it. The request already rejects a multi-source compile that
            // asks for the fallback, so Sources[0] is the whole request here.
            if (request.AllowAssemblyBuilderFallback)
            {
                return RoslynCompilerBackend.CompileAsync(
                    request.Sources[0].Path,
                    request.DllPath,
                    references,
                    paths,
                    options,
                    ct,
                    markBuildStarted: static () => { },
                    markBuildFinished: static () => { },
                    incrementBuildCount: static () => { });
            }

            List<string> sourcePaths = new List<string>(request.Sources.Count);
            foreach (HotReloadRoslynCompileSource source in request.Sources)
            {
                sourcePaths.Add(source.Path);
            }

            return RoslynCompilerBackend.CompileMultipleSourcesAsync(
                sourcePaths,
                request.DllPath,
                references,
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
            // GetTypes descends into nested types, and Cecil's FullName keeps the '/' metadata
            // separator, so a descriptor naming a nested type is matched instead of missed.
            foreach (TypeDefinition type in assembly.MainModule.GetTypes())
            {
                if (type.Name != "<Module>")
                {
                    typeNames.Add(type.FullName);
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
}
