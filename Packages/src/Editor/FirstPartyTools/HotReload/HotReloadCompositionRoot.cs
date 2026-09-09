using System;
using System.Collections.Generic;

using HarmonyLib;

using UnityEngine;

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
    /// Ownership of a domain belongs to whoever brought it in: a scope suspends and disposes only
    /// the domain it installed itself, and a replacement that shares the installed domain does
    /// neither, because the services it hands back still run on that domain.
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
            UninstallServices(_services);
            Install(CreateProductionServices());
        }

        /// <summary>
        /// Builds the services production runs on, without installing them.
        /// </summary>
        internal static HotReloadServices CreateProductionServices()
        {
            HotReloadHarmonyGateway harmony =
                new HotReloadHarmonyGateway(new Harmony(HotReloadConstants.HarmonyId));
            return CreateServices(
                CreateProductionDomain(),
                harmony,
                new HotReloadPackageRootCapture(),
                new HotReloadEditorStateSnapshotCapture(),
                TransformWorkerHost.Shared,
                HotReloadGroupProcessorDependencies.CreateProduction);
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
        internal static HotReloadServices CreateServices(
            HotReloadDomain domain,
            IHotReloadHarmony harmony,
            IHotReloadPackageRootCapture packageRootCapture,
            IHotReloadEditorStateSnapshotCapture editorStateSnapshotCapture,
            TransformWorkerHost transformWorkerHost,
            Func<HotReloadGroupStageCollaborators, HotReloadGroupProcessorDependencies>
                buildDependencies)
        {
            // Built in dependency order, and every collaborator takes what it needs here: nothing
            // below may read the installed services, or a replacement scope would leave it bound
            // to the domain that happened to be installed when it was built.
            HotReloadPatcher patcher = new HotReloadPatcher(domain, harmony);
            HotReloadFileEntryApplier fileEntryApplier = new HotReloadFileEntryApplier(domain, patcher);
            HotReloadEntryApplier entryApplier =
                new HotReloadEntryApplier(domain, patcher, fileEntryApplier);
            TransformWorkerClient transformWorkerClient = new TransformWorkerClient(transformWorkerHost);
            HotReloadGroupStageCollaborators collaborators = new HotReloadGroupStageCollaborators(
                domain,
                patcher,
                fileEntryApplier,
                entryApplier,
                transformWorkerClient,
                packageRootCapture,
                editorStateSnapshotCapture,
                new HotReloadGroupCommitPolicy(domain, fileEntryApplier));
            // A factory, not a built value: the stages are bound to the collaborators built here,
            // and a caller that built them from the installed services would bind a replacement's
            // group run back to the domain that was installed when it called.
            HotReloadGroupProcessorDependencies dependencies = buildDependencies(collaborators);
            Debug.Assert(dependencies != null, "buildDependencies must not return null.");
            HotReloadGroupCommitStage groupCommitStage = new HotReloadGroupCommitStage(
                domain,
                dependencies,
                fileEntryApplier,
                entryApplier,
                collaborators.CommitPolicy);
            HotReloadGroupProcessor groupProcessor = new HotReloadGroupProcessor(
                dependencies,
                collaborators,
                groupCommitStage);
            return new HotReloadServices(
                domain,
                harmony,
                patcher,
                fileEntryApplier,
                entryApplier,
                transformWorkerClient,
                collaborators,
                groupCommitStage,
                groupProcessor,
                new HotReloadOrchestrator(
                    domain,
                    patcher,
                    groupProcessor,
                    new HotReloadInputFileResolver(
                        domain,
                        packageRootCapture,
                        editorStateSnapshotCapture),
                    new HotReloadDeferredInputClassifier(),
                    new HotReloadSiblingRebindReporter(domain),
                    packageRootCapture),
                new HotReloadStatusExecutor(domain, patcher),
                packageRootCapture,
                editorStateSnapshotCapture,
                new HotReloadChangeDetector());
        }

        /// <summary>
        /// Installs <paramref name="replacement"/> and returns a scope that disposes it and puts
        /// the previous services back.
        /// </summary>
        internal static IDisposable BeginReplacement(HotReloadServices replacement)
        {
            Debug.Assert(replacement != null, "replacement must not be null.");
            HotReloadServices previous = _services;
            // A replacement that only substitutes a stage keeps the installed domain, so it
            // neither takes the resolver over nor owns it: suspending and disposing the one
            // domain both scopes run on would leave the restored services unable to answer binds.
            bool sharesDomain = previous != null && ReferenceEquals(previous.Domain, replacement.Domain);
            if (!sharesDomain)
            {
                // The taken-over resolver is detached before the replacement is attached, or both
                // stay subscribed and one bind reaches two domains' artifacts.
                previous?.Domain.IntroducedTypeResolver.Suspend();
            }

            Install(replacement);
            return new ReplacementScope(previous, replacement, sharesDomain);
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
            HotReloadPausePointCoordination.HotReloadSide = new HotReloadPausePointPort(domain);
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
            // The sibling tools are told "no domain installed" here too: leaving the port behind
            // would keep answering pause point from the domain the uninstall is about to dispose.
            HotReloadIntroducedTypeCoordination.DescribeActiveTypeNames = null;
            HotReloadPausePointCoordination.HotReloadSide = null;
        }

        /// <summary>
        /// Drops the installed services and their wiring, leaving the sibling tools reading the
        /// documented "no domain installed" answer. Production reaches this state only through
        /// <see cref="Initialize"/>; a test calls it to observe that state and restores with
        /// <see cref="Initialize"/>.
        /// </summary>
        internal static void UninstallInstalledServices()
        {
            HotReloadServices installed = _services;
            _services = null;
            UninstallServices(installed);
        }

        private static void UninstallServices(HotReloadServices services)
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

        private sealed class ReplacementScope : IDisposable
        {
            private readonly HotReloadServices _previous;
            private readonly HotReloadServices _installed;
            private readonly bool _sharesDomain;
            private bool _restored;

            internal ReplacementScope(
                HotReloadServices previous,
                HotReloadServices installed,
                bool sharesDomain)
            {
                _previous = previous;
                _installed = installed;
                _sharesDomain = sharesDomain;
            }

            public void Dispose()
            {
                if (_restored)
                {
                    return;
                }

                _restored = true;
                if (_sharesDomain)
                {
                    // Only the stages are put back: the domain the two services share stays alive
                    // and stays attached, so the wiring never points at nothing in between.
                    Install(_previous);
                    return;
                }

                UninstallServices(_installed);
                // Install reattaches the resolver that came back, so the replacement's has to be
                // gone by now: UninstallServices disposed it, which leaves it detached for good.
                Install(_previous);
            }
        }
    }
}
