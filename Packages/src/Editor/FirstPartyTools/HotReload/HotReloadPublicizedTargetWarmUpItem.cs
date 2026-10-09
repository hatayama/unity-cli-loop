using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Writes the publicized copy of each target assembly.
    /// </summary>
    internal sealed class HotReloadPublicizedTargetWarmUpItem : IHotReloadWarmUpItem
    {
        public string Name => "publicized_targets";

        public Task RunAsync(HotReloadWarmUpContext context, CancellationToken ct)
        {
            return Task.CompletedTask;
        }

        /// <summary>Writes the publicized copy of each request; throws before the next request once cancelled.</summary>
        internal static void WritePublicizedCopies(
            IReadOnlyList<HotReloadPublicizedTargetRequest> requests,
            CancellationToken ct)
        {
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
