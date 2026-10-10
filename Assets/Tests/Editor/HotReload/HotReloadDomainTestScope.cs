using System;
using System.Threading.Tasks;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Installs a hot-reload services graph of its own for the duration of one test and takes it
    /// back out again, so a test arranges patches and generations without inheriting or leaving any.
    /// </summary>
    /// <remarks>
    /// Why a scope rather than a revert in SetUp and TearDown: reverting empties the one production
    /// domain but leaves every test sharing it, so a class that forgets a store leaks into the next
    /// one. A scope that brings its own domain cannot leak, because the domain goes away with it.
    /// Substituting a single collaborator on the installed graph is a different need, and
    /// <see cref="HotReloadServicesTestScope"/> covers that one.
    /// </remarks>
    internal sealed class HotReloadDomainTestScope : IDisposable
    {
        private readonly HotReloadServices _services;
        private readonly IDisposable _replacement;
        private bool _disposed;

        internal HotReloadDomainTestScope()
            : this(HotReloadCompositionRoot.CreateProductionServices())
        {
        }

        private HotReloadDomainTestScope(HotReloadServices services)
        {
            // Why stop the installed warm-up and backfill first: they read the shared caches on a
            // pool thread, and a test that counts their reads must not race them. The scope's own
            // run yields to the scope's warm-up and backfill, never to the installed ones.
            HotReloadServices installed = HotReloadCompositionRoot.Services;
            InstalledWarmUpStopped = Task.WhenAll(
                installed.WarmUp.Shutdown(HotReloadConstants.WarmUpShutdownTriggerTestScope),
                installed.CompiledCallers.StopBackgroundReadsAsync(HotReloadConstants.WarmUpShutdownTriggerTestScope));

            // The capture is main-thread only, and a run inside the scope normalizes script paths
            // against these roots on the background threads it switches to.
            services.PackageRootCapture.CaptureCurrent();
            _services = services;
            _replacement = HotReloadCompositionRoot.BeginReplacement(services);
        }

        /// <summary>
        /// Completes once the warm-up and the caller-note backfill that were installed before this
        /// scope have stopped. A test that counts reads of a shared cache awaits it before it
        /// clears the cache.
        /// </summary>
        internal Task InstalledWarmUpStopped { get; }

        /// <summary>A scope whose services run <paramref name="warmUp"/> instead of a production one.</summary>
        internal static HotReloadDomainTestScope WithWarmUp(HotReloadWarmUp warmUp)
        {
            return new HotReloadDomainTestScope(
                HotReloadCompositionRoot.CreateProductionServicesWith(
                    warmUp,
                    HotReloadCompiledCallers.CreateProduction()));
        }

        /// <summary>
        /// A scope whose services run <paramref name="callSiteBackfill"/> instead of a production
        /// one, with an inert warm-up.
        /// </summary>
        internal static HotReloadDomainTestScope WithCallSiteBackfill(HotReloadCallSiteBackfill callSiteBackfill)
        {
            return new HotReloadDomainTestScope(
                HotReloadCompositionRoot.CreateProductionServicesWith(
                    HotReloadWarmUpTestDoubles.CreateInert(),
                    new HotReloadCompiledCallers(HotReloadCompiledCallSiteCache.Shared, callSiteBackfill)));
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            // Harmony first: reverting after the domain is put back would unpatch against the
            // generations the restored services own, not the ones this scope patched.
            _services.Patcher.RevertAll();
            // The proxies this scope's runs attached are components in the open scene, and the
            // domain that owns their types goes away with the scope, so they come off here.
            _services.UnityMessageForwarding.Clear();
            _replacement.Dispose();
        }
    }
}
