using System;
using System.Collections.Generic;

using HarmonyLib;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Builds the hot-reload services of this Unity domain and installs them: the gateways the
    /// emitted IL calls into, and the coordination delegates the sibling tools read.
    /// </summary>
    /// <remarks>
    /// Why one place does the installing: the gateways and the delegates all have to point at the
    /// same domain, and a test that swaps the domain has to move every one of them together.
    /// Invariant: at most one hot-reload introduced type resolver is subscribed to
    /// AppDomain.AssemblyResolve at a time, so a single bind is never answered from two domains'
    /// artifacts. A replacement scope keeps it by suspending the resolver it takes over from.
    /// </remarks>
    internal static class HotReloadCompositionRoot
    {
        private static HotReloadServices _services;

        internal static HotReloadServices Services =>
            _services ?? throw new InvalidOperationException(
                "HotReloadCompositionRoot.Initialize has not run in this domain.");

        /// <summary>
        /// Builds and installs the production services. Called once per domain reload from editor
        /// startup.
        /// </summary>
        public static void Initialize()
        {
            // Uninstalling first because a second Initialize would otherwise leave the previous
            // domain's resolver attached to AppDomain.AssemblyResolve with nothing owning it.
            Uninstall(_services);
            Install(CreateProductionServices());
        }

        /// <summary>
        /// Builds the services production runs on, without installing them.
        /// </summary>
        internal static HotReloadServices CreateProductionServices()
        {
            HotReloadHarmonyGateway harmony =
                new HotReloadHarmonyGateway(new Harmony(HotReloadConstants.HarmonyId));
            return CreateServices(CreateProductionDomain(), harmony);
        }

        /// <summary>
        /// Builds an empty domain of the shape production runs on. A test that has to substitute
        /// the patch engine builds the domain through this and passes its own harmony to
        /// <see cref="CreateServices"/>.
        /// </summary>
        internal static HotReloadDomain CreateProductionDomain()
        {
            HotReloadIntroducedTypeRegistry registry = new HotReloadIntroducedTypeRegistry();
            // The resolver comes back detached and answers no bind until Install attaches it, so
            // building a domain here can never race the resolver that is still installed.
            HotReloadIntroducedTypeAssemblyResolver resolver =
                new HotReloadIntroducedTypeAssemblyResolver(registry);
            return new HotReloadDomain(registry, resolver);
        }

        /// <summary>
        /// Builds the services around <paramref name="domain"/> and <paramref name="harmony"/>,
        /// without installing them. A test replaces the patch engine through this.
        /// </summary>
        internal static HotReloadServices CreateServices(HotReloadDomain domain, IHotReloadHarmony harmony)
        {
            HotReloadPatcher patcher = new HotReloadPatcher(domain, harmony);
            HotReloadFileEntryApplier fileEntryApplier = new HotReloadFileEntryApplier(domain, patcher);
            return new HotReloadServices(
                domain,
                harmony,
                patcher,
                fileEntryApplier,
                new HotReloadEntryApplier(domain, patcher, fileEntryApplier));
        }

        /// <summary>
        /// Installs <paramref name="replacement"/> and returns a scope that disposes it and puts
        /// the previous services back.
        /// </summary>
        internal static IDisposable BeginReplacement(HotReloadServices replacement)
        {
            HotReloadServices previous = _services;
            // The taken-over resolver is detached before the replacement is attached, or both
            // stay subscribed and one bind reaches two domains' artifacts.
            previous?.Domain.IntroducedTypeResolver.Suspend();
            Install(replacement);
            return new ReplacementScope(previous, replacement);
        }

        private static void Install(HotReloadServices services)
        {
            _services = services;
            if (services == null)
            {
                ClearWiring();
                return;
            }

            HotReloadDomain domain = services.Domain;
            HotReloadAddedFieldStore.Current = domain.AddedFieldValues;
            HotReloadInvocationRegistry.Current = domain.Invocations;
            HotReloadTranspilerDomainGateway.Current = domain;
            HotReloadIntroducedTypeCoordination.DescribeActiveTypeNames =
                () => DescribeActiveTypeNames(domain);
            HotReloadPausePointCoordination.GetShimLookupForFile = domain.LookupShimsForFile;
            HotReloadPausePointCoordination.GetVerifiedSnapshotSourceForFile =
                domain.LoadVerifiedSnapshotSourceForFile;
            HotReloadPausePointCoordination.GetVerifiedSnapshotSource = LoadVerifiedSnapshotSource;
            HotReloadPausePointCoordination.GetAddedFieldsForType = domain.GetAddedFieldsForType;
            HotReloadPausePointCoordination.GetActiveShimForMethod =
                method => domain.FindGenerationForMethod(method)?.FindPatchShim(method);
            HotReloadPausePointCoordination.GetTransplantLocals =
                method => domain.FindGenerationForMethod(method)?.FindTransplantLocals(method);
            HotReloadPausePointCoordination.GetTransplantPreambleLength =
                method => domain.FindGenerationForMethod(method)?.FindTransplantPreambleLength(method) ?? 0;
            // Attaching last keeps the invariant across the gap: the resolver only starts
            // answering binds once every gateway already points at the domain behind it.
            domain.IntroducedTypeResolver.Resume();
        }

        // Why the gateways are emptied rather than left pointing at a disposed domain: emitted IL
        // and the sibling tools call them at any time, and a null slot is the documented
        // "no domain installed" answer they already handle.
        private static void ClearWiring()
        {
            HotReloadAddedFieldStore.Current = null;
            HotReloadInvocationRegistry.Current = null;
            HotReloadTranspilerDomainGateway.Current = null;
        }

        private static void Uninstall(HotReloadServices services)
        {
            ClearWiring();
            // Why the domain is disposed after the wiring is dropped: disposing unsubscribes its
            // resolver, and a bind arriving through a still-installed gateway would otherwise
            // reach a domain that can no longer answer.
            services?.Domain.Dispose();
        }

        private static IReadOnlyList<string> DescribeActiveTypeNames(HotReloadDomain domain)
        {
            List<string> names = new List<string>();
            foreach (HotReloadIntroducedTypeDescriptor descriptor in domain.IntroducedTypes.DescribeActive())
            {
                names.Add(descriptor.MetadataName.Value);
            }

            return names;
        }

        private static string LoadVerifiedSnapshotSource(string projectRelativeFile, string dllPath)
        {
            if (string.IsNullOrEmpty(projectRelativeFile) || string.IsNullOrEmpty(dllPath))
            {
                return null;
            }

            return HotReloadSourceBaseline.LoadVerifiedSnapshotSource(projectRelativeFile, dllPath);
        }

        private sealed class ReplacementScope : IDisposable
        {
            private readonly HotReloadServices _previous;
            private readonly HotReloadServices _installed;
            private bool _restored;

            internal ReplacementScope(HotReloadServices previous, HotReloadServices installed)
            {
                _previous = previous;
                _installed = installed;
            }

            public void Dispose()
            {
                if (_restored)
                {
                    return;
                }

                _restored = true;
                Uninstall(_installed);
                // Install reattaches the resolver that came back, so the replacement's has to be
                // gone by now: Uninstall disposed it, which leaves it detached for good.
                Install(_previous);
            }
        }
    }
}
