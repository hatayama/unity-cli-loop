using System;
using System.Collections.Generic;
using System.Reflection;

using HarmonyLib;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Covers who owns the domain a replacement scope installs, which decides whether closing the
    /// scope may dispose it.
    /// </summary>
    public class HotReloadCompositionRootTests
    {
        /// <summary>
        /// A replacement that shares the installed domain leaves it alive and answering binds
        /// after the scope closes, because it never owned that domain.
        /// </summary>
        [Test]
        public void BeginReplacement_WhenTheReplacementSharesTheInstalledDomain_LeavesItUsableAfterTheScope()
        {
            using (HotReloadCompositionRoot.BeginReplacement(HotReloadCompositionRoot.CreateProductionServices()))
            {
                HotReloadDomain sharedDomain = HotReloadCompositionRoot.Services.Domain;
                HotReloadIntroducedTypeArtifact artifact = CreateArtifact("Example.Shared");
                sharedDomain.IntroducedTypes.RegisterPrepared(artifact);
                sharedDomain.IntroducedTypes.Activate(artifact);

                using (HotReloadCompositionRoot.BeginReplacement(CreateServicesSharing(sharedDomain)))
                {
                    Assert.That(HotReloadCompositionRoot.Services.Domain, Is.SameAs(sharedDomain));
                }

                Assert.That(HotReloadCompositionRoot.Services.Domain, Is.SameAs(sharedDomain));
                Assert.That(
                    sharedDomain.IntroducedTypeResolver.ResolveExact(artifact.AssemblyFullName),
                    Is.SameAs(artifact.Assembly));
                // A disposed resolver refuses to detach or reattach, so this is what tells a live
                // domain apart from one the inner scope disposed on its way out.
                Assert.DoesNotThrow(
                    () =>
                    {
                        sharedDomain.IntroducedTypeResolver.Suspend();
                        sharedDomain.IntroducedTypeResolver.Resume();
                    });
            }
        }

        /// <summary>
        /// A replacement that brings its own domain owns it, so closing the scope disposes that
        /// domain and puts the previous services back.
        /// </summary>
        [Test]
        public void BeginReplacement_WhenTheReplacementBringsItsOwnDomain_DisposesItAndRestoresThePrevious()
        {
            HotReloadServices previous = HotReloadCompositionRoot.Services;
            HotReloadDomain ownedDomain = HotReloadCompositionRoot.CreateProductionDomain();

            using (HotReloadCompositionRoot.BeginReplacement(CreateServicesOwning(ownedDomain)))
            {
                Assert.That(HotReloadCompositionRoot.Services.Domain, Is.SameAs(ownedDomain));
            }

            Assert.That(HotReloadCompositionRoot.Services, Is.SameAs(previous));
            Assert.Throws<ObjectDisposedException>(() => ownedDomain.IntroducedTypeResolver.Suspend());
        }

        /// <summary>
        /// Uninstalling clears the coordination point, so the pause point side reads the
        /// documented "no domain installed" answer instead of a port that would keep answering
        /// from the domain the uninstall just disposed.
        /// </summary>
        [Test]
        public void UninstallInstalledServices_LeavesTheSiblingToolsReadingNoDomainInstalled()
        {
            IHotReloadPausePointPort installedPort = HotReloadPausePointCoordination.HotReloadSide;
            Assert.That(installedPort, Is.Not.Null, "the production services must be installed here.");

            try
            {
                HotReloadCompositionRoot.UninstallInstalledServices();

                Assert.That(HotReloadPausePointCoordination.HotReloadSide, Is.Null);
                Assert.That(HotReloadIntroducedTypeCoordination.DescribeActiveTypeNames, Is.Null);
            }
            finally
            {
                // The uninstall disposed the domain the rest of the suite runs on, so the next
                // test needs the production services back.
                HotReloadCompositionRoot.Initialize();
            }

            Assert.That(HotReloadPausePointCoordination.HotReloadSide, Is.Not.Null);
            Assert.That(HotReloadPausePointCoordination.HotReloadSide, Is.Not.SameAs(installedPort));
        }

        private static HotReloadServices CreateServicesSharing(HotReloadDomain domain)
        {
            HotReloadServices installed = HotReloadCompositionRoot.Services;
            return HotReloadCompositionRoot.CreateServices(
                domain,
                installed.Harmony,
                installed.PackageRootCapture,
                installed.EditorStateSnapshotCapture,
                TransformWorkerHost.Shared,
                HotReloadGroupProcessorDependencies.CreateProduction);
        }

        private static HotReloadServices CreateServicesOwning(HotReloadDomain domain)
        {
            return HotReloadCompositionRoot.CreateServices(
                domain,
                new HotReloadHarmonyGateway(new Harmony(HotReloadConstants.HarmonyId)),
                new HotReloadPackageRootCapture(),
                new HotReloadEditorStateSnapshotCapture(),
                TransformWorkerHost.Shared,
                HotReloadGroupProcessorDependencies.CreateProduction);
        }

        private static HotReloadIntroducedTypeArtifact CreateArtifact(string metadataName)
        {
            Assembly assembly = typeof(HotReloadCompositionRootTests).Assembly;
            List<HotReloadIntroducedTypeDescriptor> descriptors =
                new List<HotReloadIntroducedTypeDescriptor>
                {
                    new HotReloadIntroducedTypeDescriptor(
                        "OriginalAssembly",
                        "original-mvid",
                        metadataName,
                        "Assets/Example.cs",
                        "fingerprint",
                        "public class Introduced { }")
                };
            return new HotReloadIntroducedTypeArtifact(
                assembly,
                assembly.Location,
                assembly.Location,
                descriptors);
        }
    }
}
