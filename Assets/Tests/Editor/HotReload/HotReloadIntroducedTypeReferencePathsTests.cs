using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;

using NUnit.Framework;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Covers the coordination point a tool that compiles against file paths reads the active
    /// introduced-type assemblies through, including the states in which it must stay silent.
    /// </summary>
    public class HotReloadIntroducedTypeReferencePathsTests
    {
        private HotReloadDomainTestScope _scope;
        private string _existingDllPath;

        [SetUp]
        public void SetUp()
        {
            _scope = new HotReloadDomainTestScope();
            _existingDllPath = Path.Combine(
                Application.temporaryCachePath,
                "HotReloadIntroducedTypeReferencePathsTests.dll");
            File.WriteAllBytes(_existingDllPath, new byte[] { 0x4D, 0x5A });
        }

        [TearDown]
        public void TearDown()
        {
            if (File.Exists(_existingDllPath))
            {
                File.Delete(_existingDllPath);
            }

            _scope.Dispose();
            VibeLogger.ClearMemoryLogs();
        }

        /// <summary>
        /// What: an active artifact whose dll is on disk is offered as a reference, while an active
        /// artifact whose dll has gone is left out, because a compiler cannot open a file that is
        /// no longer there.
        /// </summary>
        [Test]
        public void DescribeActiveArtifactReferencePaths_LeavesOutAnArtifactWhoseDllIsGone()
        {
            HotReloadIntroducedTypeRegistry registry =
                HotReloadCompositionRoot.Services.Domain.IntroducedTypes;
            ActivateArtifact(registry, "onDisk", _existingDllPath);
            ActivateArtifact(registry, "missing", Path.Combine(Application.temporaryCachePath, "NotWritten.dll"));

            IReadOnlyList<string> paths =
                HotReloadIntroducedTypeCoordination.DescribeActiveArtifactReferencePaths();

            Assert.That(paths, Is.EqualTo(new[] { _existingDllPath }));
        }

        /// <summary>
        /// What: reverting the applied patches keeps the introduced types, so the reference stays on
        /// offer — a revert deliberately does not take an introduced type back out.
        /// </summary>
        [Test]
        public void DescribeActiveArtifactReferencePaths_AfterRevertingThePatches_StillOffersTheArtifact()
        {
            HotReloadIntroducedTypeRegistry registry =
                HotReloadCompositionRoot.Services.Domain.IntroducedTypes;
            ActivateArtifact(registry, "onDisk", _existingDllPath);

            HotReloadCompositionRoot.Services.Patcher.RevertAll();

            Assert.That(
                HotReloadIntroducedTypeCoordination.DescribeActiveArtifactReferencePaths(),
                Is.EqualTo(new[] { _existingDllPath }));
        }

        private static void ActivateArtifact(
            HotReloadIntroducedTypeRegistry registry,
            string fingerprint,
            string dllPath)
        {
            AssemblyName assemblyName = new AssemblyName(
                "HotReloadIntroducedTypeReferencePaths." + fingerprint);
            HotReloadIntroducedTypeArtifact artifact = new HotReloadIntroducedTypeArtifact(
                AssemblyBuilder.DefineDynamicAssembly(assemblyName, AssemblyBuilderAccess.Run),
                dllPath,
                Path.ChangeExtension(dllPath, ".pdb"),
                new List<HotReloadIntroducedTypeDescriptor>
                {
                    new HotReloadIntroducedTypeDescriptor(
                        "OriginalAssembly",
                        "original-mvid",
                        "Example.Introduced" + fingerprint,
                        "Assets/Example" + fingerprint + ".cs",
                        fingerprint,
                        "public class Introduced" + fingerprint + " { }")
                });
            registry.RegisterPrepared(artifact);
            registry.Activate(artifact);
        }
    }
}
