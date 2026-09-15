using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// The side-effect boundary of hot-reload Roslyn compilation — compiler discovery, source
    /// writing, the backend call, and reading back what it produced — so a test can drive the
    /// compile sequence without a Unity installation or a real assembly on disk.
    /// </summary>
    internal interface IHotReloadRoslynCompilerEnvironment
    {
        Task<ExternalCompilerPaths> ResolveCompilerPathsOnMainThreadAsync(CancellationToken ct);

        Task<DynamicCompilationBackendResult> CompileAsync(
            HotReloadRoslynCompileRequest request,
            ExternalCompilerPaths paths,
            CancellationToken ct);

        bool FileExists(string path);

        AssemblyName ReadAssemblyName(string path);

        byte[] ReadAllBytes(string path);

        CompiledAssemblyLoadResult Load(byte[] assemblyBytes, byte[] pdbBytes);

        IReadOnlyCollection<string> ReadDefinedTypeNames(string path);

        void WriteSource(string path, string source);
    }
}
