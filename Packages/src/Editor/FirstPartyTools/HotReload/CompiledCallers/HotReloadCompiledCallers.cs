using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// The compiled-caller analysis of hot reload: finds the compiled methods that call a method,
    /// tells whether those callers are one-shot lifecycle messages, and keeps the compiled
    /// assemblies it read cached between runs. What a run could not read is read in the background
    /// after it. Begin every run with <see cref="BeginRunAsync"/>.
    /// </summary>
    public sealed class HotReloadCompiledCallers
    {
        private readonly HotReloadCompiledCallSiteCache _cache;
        private readonly HotReloadCallSiteBackfill _backfill;

        /// <param name="cache">The cache a run holds and the call-site warm-up fills; production passes the shared cache the caller scan reads.</param>
        /// <param name="backfill">The background reader of the dlls a run's notes could not read.</param>
        internal HotReloadCompiledCallers(HotReloadCompiledCallSiteCache cache, HotReloadCallSiteBackfill backfill)
        {
            Debug.Assert(cache != null, "cache must not be null.");
            Debug.Assert(backfill != null, "backfill must not be null.");
            _cache = cache;
            _backfill = backfill;
        }

        /// <summary>The background reader, for tests that await or inspect it.</summary>
        internal HotReloadCallSiteBackfill Backfill => _backfill;

        /// <summary>Builds the instance production runs on. Call on the Unity main thread.</summary>
        public static HotReloadCompiledCallers CreateProduction()
        {
            // Why the cache is taken here: this runs on the main thread while the services are built,
            // and no pool thread may be the first to touch a Shared instance.
            HotReloadCompiledCallSiteCache cache = HotReloadCompiledCallSiteCache.Shared;
            HotReloadCallSiteBackfill backfill = new HotReloadCallSiteBackfill(
                (dllPath, ct) => Task.Run(() => cache.GetOrLoad(dllPath), ct));
            return new HotReloadCompiledCallers(cache, backfill);
        }

        /// <summary>
        /// Finds the compiled call and ldftn sites that reference any of <paramref name="targets"/>,
        /// reading every compiled assembly it needs.
        /// </summary>
        public List<HotReloadCallSiteHit> FindCallSites(string projectRoot, HotReloadCompiledMethodIdentity[] targets)
        {
            return HotReloadCallSiteScanner.FindCallSites(projectRoot, targets).Hits;
        }

        /// <summary>The compiled dlls of the assemblies that reference <paramref name="assemblyName"/>.</summary>
        public IReadOnlyList<string> CollectReferencingDllPaths(string projectRoot, string assemblyName)
        {
            return HotReloadCallSiteScanner.CollectReferencingDllPaths(projectRoot, assemblyName);
        }

        /// <summary>
        /// Reads the call sites of <paramref name="dllPaths"/> into the cache on a pool thread; throws
        /// before the next dll once <paramref name="ct"/> is cancelled.
        /// </summary>
        public Task WarmUpCallSitesAsync(IReadOnlyList<string> dllPaths, CancellationToken ct)
        {
            HotReloadCompiledCallSiteCache cache = _cache;
            return Task.Run(() => LoadCallSites(dllPaths, cache, ct));
        }

        /// <summary>Loads each dll and returns how many it loaded; throws before the next dll once cancelled.</summary>
        internal static int LoadCallSites(
            IReadOnlyList<string> dllPaths,
            HotReloadCompiledCallSiteCache cache,
            CancellationToken ct)
        {
            int count = 0;
            foreach (string dllPath in dllPaths)
            {
                // Why throw rather than stop: an item that returns normally is reported done, and
                // one that skipped targets has not loaded them.
                ct.ThrowIfCancellationRequested();
                cache.GetOrLoad(dllPath);
                count++;
            }

            return count;
        }

        /// <summary>
        /// Reads the referenced-method set of each of <paramref name="dllPaths"/> on a pool thread;
        /// throws before the next dll once <paramref name="ct"/> is cancelled.
        /// </summary>
        public Task WarmUpReferencedMethodSetsAsync(IReadOnlyList<string> dllPaths, CancellationToken ct)
        {
            // Why taken here: the Shared instance reads Application.dataPath when it is first
            // touched, which only the main thread may do.
            HotReloadReferencedMethodIndex index = HotReloadReferencedMethodIndex.Shared;
            return Task.Run(() => PreloadReferencedMethodSets(dllPaths, index, ct));
        }

        /// <summary>Preloads each dll's set; throws before the next dll once cancelled.</summary>
        internal static void PreloadReferencedMethodSets(
            IReadOnlyList<string> dllPaths,
            HotReloadReferencedMethodIndex index,
            CancellationToken ct)
        {
            foreach (string dllPath in dllPaths)
            {
                // Why throw rather than stop: an item that returns normally is reported done.
                ct.ThrowIfCancellationRequested();
                index.Preload(dllPath);
            }
        }

        /// <summary>
        /// Stops the background reads for <paramref name="trigger"/>; the task completes once the
        /// dll in flight has been read, and never faults.
        /// </summary>
        public Task StopBackgroundReadsAsync(string trigger)
        {
            return _backfill.Shutdown(trigger);
        }

        /// <summary>
        /// Begins a run: waits for a background read in flight to end, then keeps every cached
        /// compiled assembly cached until the returned run ends. Never faults.
        /// </summary>
        public async Task<HotReloadCompiledCallersRun> BeginRunAsync()
        {
            // Why the background reads stop before the hold: a read holds the cache lock for the
            // whole read, so a hold taken first would wait for it unmeasured; waiting here is measured
            // with the run's other yields, and the run reads nothing the background read is reading.
            await _backfill.YieldToRunAsync().ConfigureAwait(false);
            // Why the whole run: the signature-change gate during analysis and the caller notes at
            // the end both scan callers through the shared cache, once per method they check, so
            // one hold keeps every dll they read until the run ends.
            return new HotReloadCompiledCallersRun(_cache.HoldEntriesForRun(), _backfill);
        }
    }
}
