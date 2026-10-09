using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using UnityEngine;

using UnityCompilationAssembly = UnityEditor.Compilation.Assembly;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Writes the publicized copy of each target assembly. It makes the same copy a run asks
    /// <see cref="ReferencePublicizer.GetOrCreatePublicizedCopy"/> for after the reload, so the run
    /// reuses it.
    /// </summary>
    internal sealed class HotReloadPublicizedTargetWarmUpItem : IHotReloadWarmUpItem
    {
        public string Name => "publicized_targets";

        public Task RunAsync(HotReloadWarmUpContext context, CancellationToken ct)
        {
            // Why gather every target here first: the compilation assembly lookup may only run on
            // the main thread, and the search directories are collected where the run collects
            // them. Only the rewrite and the write, the costly part, go to the pool.
            List<HotReloadPublicizedTargetRequest> requests = new List<HotReloadPublicizedTargetRequest>();
            foreach (HotReloadWarmUpTarget target in context.Targets)
            {
                UnityCompilationAssembly assembly = HotReloadCompilationAssemblies.FindByName(target.AssemblyName);
                // A name left in the ledger from an older layout, or an empty list during a
                // compile. A run does not ask for a copy of that name either.
                if (assembly == null)
                {
                    continue;
                }

                IReadOnlyCollection<string> resolverSearchDirectories =
                    HotReloadResolverSearchDirectories.Collect(context.ProjectRoot, assembly);
                requests.Add(new HotReloadPublicizedTargetRequest(
                    HotReloadTypeHome.ScriptAssemblies(target.AssemblyName, target.DllPath),
                    resolverSearchDirectories));
            }

            return Task.Run(() => WritePublicizedCopies(requests, ct));
        }

        /// <summary>
        /// Writes the publicized copy of each request, or reuses the one in place; throws before the
        /// next request once cancelled. Runs on a pool thread: the publicizer reads only
        /// Application.dataPath and Application.platform, which Unity reads thread-safely.
        /// </summary>
        internal static void WritePublicizedCopies(
            IReadOnlyList<HotReloadPublicizedTargetRequest> requests,
            CancellationToken ct)
        {
            foreach (HotReloadPublicizedTargetRequest request in requests)
            {
                // Why throw rather than stop: an item that returns normally is reported done.
                ct.ThrowIfCancellationRequested();
                ReferencePublicizer.GetOrCreatePublicizedCopy(request.Home, request.ResolverSearchDirectories);
            }
        }
    }

    /// <summary>
    /// One target's input to the publicizer, gathered on the main thread.
    /// </summary>
    internal sealed class HotReloadPublicizedTargetRequest
    {
        internal HotReloadPublicizedTargetRequest(
            HotReloadTypeHome home,
            IReadOnlyCollection<string> resolverSearchDirectories)
        {
            Debug.Assert(home != null, "home must not be null.");
            Debug.Assert(resolverSearchDirectories != null, "resolverSearchDirectories must not be null.");
            Home = home;
            ResolverSearchDirectories = resolverSearchDirectories;
        }

        internal HotReloadTypeHome Home { get; }

        internal IReadOnlyCollection<string> ResolverSearchDirectories { get; }
    }
}
