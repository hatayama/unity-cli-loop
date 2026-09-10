using System;
using System.Threading;
using System.Threading.Tasks;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Runs one hot-reload Roslyn compilation: resolve the external compiler, write the sources,
    /// call the backend. Both hot-reload compile paths — the shim assembly and the retained
    /// introduced-type artifact — go through here, so the step order and the "compiler paths
    /// unresolved" outcome are decided once instead of being restated by each caller.
    /// </summary>
    internal sealed class HotReloadRoslynCompiler
    {
        private readonly IHotReloadRoslynCompilerEnvironment environment;

        public HotReloadRoslynCompiler(IHotReloadRoslynCompilerEnvironment environment)
        {
            this.environment = environment ?? throw new ArgumentNullException(nameof(environment));
        }

        /// <summary>
        /// Compiles the request's sources, or reports that the compiler paths were unresolved.
        /// </summary>
        public async Task<HotReloadRoslynCompileOutcome> CompileAsync(
            HotReloadRoslynCompileRequest request,
            CancellationToken ct)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            ExternalCompilerPaths paths = await environment.ResolveCompilerPathsOnMainThreadAsync(ct)
                .ConfigureAwait(false);
            // Why nothing is written before this point: an unresolvable compiler must leave the
            // caller's work directory exactly as it was, so a later attempt cannot mistake a
            // half-populated directory for the output of a compile that ran.
            if (paths == null)
            {
                return HotReloadRoslynCompileOutcome.PathsUnresolved();
            }

            foreach (HotReloadRoslynCompileSource source in request.Sources)
            {
                environment.WriteSource(source.Path, source.Text);
            }

            return HotReloadRoslynCompileOutcome.Compiled(
                await environment.CompileAsync(request, paths, ct).ConfigureAwait(false));
        }
    }
}
