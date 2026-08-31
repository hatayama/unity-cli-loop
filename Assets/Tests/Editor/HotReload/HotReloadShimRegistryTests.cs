using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// EditMode coverage for shim registration exposed through hot-reload / pause-point
    /// coordination after orchestrated apply / revert.
    /// </summary>
    public class HotReloadShimRegistryTests
    {
        private const string FixtureProjectRelativePath =
            "Assets/Tests/Editor/HotReload/HotReloadE2EFixtures.cs";

        [TearDown]
        public void TearDown()
        {
            HotReloadPatcher.RevertAll();
        }

        /// <summary>
        /// What: after a successful orchestrated apply, GetShimLookupForFile returns the
        /// patched method for the fixture path (relative and absolute), with PDB bytes and
        /// transplant (non-delegation) shape for ComputeWithPrivate.
        /// </summary>
        [Test]
        public async Task Apply_ThenGetShimLookupForFile_ReturnsPatchedMethods()
        {
            await PatchComputeWithPrivateAsync();

            HotReloadShimFileLookup lookup =
                HotReloadPausePointCoordination.GetShimLookupForFile?.Invoke(FixtureProjectRelativePath);
            Assert.That(lookup, Is.Not.Null);
            Assert.That(lookup.AssemblyBytes, Is.Not.Null.And.Not.Empty);
            Assert.That(lookup.PdbBytes, Is.Not.Null.And.Not.Empty);
            Assert.That(lookup.LoadedAssembly, Is.Not.Null);
            Assert.That(lookup.Methods, Is.Not.Empty);

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string absoluteFixturePath = Path.GetFullPath(
                Path.Combine(projectRoot, FixtureProjectRelativePath));
            HotReloadShimFileLookup absoluteLookup =
                HotReloadPausePointCoordination.GetShimLookupForFile?.Invoke(absoluteFixturePath);
            Assert.That(
                absoluteLookup,
                Is.Not.Null,
                "Absolute --file-style paths must resolve via PathsReferToSameFile linear scan.");

            bool foundCompute = false;
            foreach (HotReloadShimMethodLookup method in lookup.Methods)
            {
                if (method.OriginalMethod != null
                    && method.OriginalMethod.Name == nameof(HotReloadE2EFixture.ComputeWithPrivate))
                {
                    foundCompute = true;
                    Assert.That(method.ShimMethod, Is.Not.Null);
                    Assert.That(method.IsDelegation, Is.False);
                    Assert.That(method.SourceStartLine, Is.GreaterThan(0));
                    Assert.That(method.SourceEndLine, Is.GreaterThanOrEqualTo(method.SourceStartLine));
                }
            }

            Assert.That(foundCompute, Is.True, "ComputeWithPrivate missing from shim lookup Methods.");
        }

        /// <summary>
        /// What: reverting ComputeWithPrivate removes only that method from GetShimLookupForFile
        /// while sibling methods that shared the edited statement remain registered.
        /// </summary>
        [Test]
        public async Task Revert_RemovesMethodFromShimLookup()
        {
            await PatchComputeWithPrivateAsync();
            MethodInfo computeMethod = typeof(HotReloadE2EFixture).GetMethod(
                nameof(HotReloadE2EFixture.ComputeWithPrivate),
                BindingFlags.Instance | BindingFlags.Public);
            Assert.That(computeMethod, Is.Not.Null);

            HotReloadShimFileLookup beforeLookup =
                HotReloadPausePointCoordination.GetShimLookupForFile?.Invoke(FixtureProjectRelativePath);
            Assert.That(beforeLookup, Is.Not.Null);
            Assert.That(
                beforeLookup.Methods.Count,
                Is.GreaterThanOrEqualTo(2),
                "Precondition: Replace of `return _secret + delta;` must patch ComputeWithPrivate " +
                "plus at least one sibling so revert cannot clear the whole file lookup.");

            bool reverted = HotReloadPatcher.Revert(computeMethod);
            Assert.That(reverted, Is.True);

            HotReloadShimFileLookup lookup =
                HotReloadPausePointCoordination.GetShimLookupForFile?.Invoke(FixtureProjectRelativePath);
            Assert.That(lookup, Is.Not.Null);
            Assert.That(lookup.Methods, Is.Not.Empty);

            foreach (HotReloadShimMethodLookup method in lookup.Methods)
            {
                Assert.That(
                    method.OriginalMethod.Name,
                    Is.Not.EqualTo(nameof(HotReloadE2EFixture.ComputeWithPrivate)));
            }
        }

        /// <summary>
        /// What: RevertAll clears every shim registration so GetShimLookupForFile returns null.
        /// </summary>
        [Test]
        public async Task RevertAll_ClearsShimLookup()
        {
            await PatchComputeWithPrivateAsync();
            HotReloadPatcher.RevertAll();

            HotReloadShimFileLookup lookup =
                HotReloadPausePointCoordination.GetShimLookupForFile?.Invoke(FixtureProjectRelativePath);
            Assert.That(lookup, Is.Null);
        }

        /// <summary>
        /// What: re-applying a newer edit replaces the LoadedAssembly identity for that file.
        /// </summary>
        [Test]
        public async Task ReApply_ReplacesLoadedAssembly()
        {
            await PatchComputeWithPrivateAsync(extraDelta: 100);
            HotReloadShimFileLookup firstLookup =
                HotReloadPausePointCoordination.GetShimLookupForFile?.Invoke(FixtureProjectRelativePath);
            Assert.That(firstLookup, Is.Not.Null);
            Assembly firstAssembly = firstLookup.LoadedAssembly;

            await PatchComputeWithPrivateAsync(extraDelta: 200);
            HotReloadShimFileLookup secondLookup =
                HotReloadPausePointCoordination.GetShimLookupForFile?.Invoke(FixtureProjectRelativePath);
            Assert.That(secondLookup, Is.Not.Null);
            Assert.That(
                ReferenceEquals(secondLookup.LoadedAssembly, firstAssembly),
                Is.False,
                "Re-apply must load a new shim assembly generation.");
        }

        /// <summary>
        /// What: GetTransplantLocals returns a list whose length matches the shim method's
        /// LocalVariables count for a transplant-patched method.
        /// </summary>
        [Test]
        public async Task Apply_Transplant_ExposesTransplantLocals()
        {
            await PatchComputeWithPrivateAsync();
            MethodInfo computeMethod = typeof(HotReloadE2EFixture).GetMethod(
                nameof(HotReloadE2EFixture.ComputeWithPrivate),
                BindingFlags.Instance | BindingFlags.Public);
            Assert.That(computeMethod, Is.Not.Null);

            HotReloadShimFileLookup lookup =
                HotReloadPausePointCoordination.GetShimLookupForFile?.Invoke(FixtureProjectRelativePath);
            Assert.That(lookup, Is.Not.Null);

            MethodBase shimMethod = null;
            foreach (HotReloadShimMethodLookup method in lookup.Methods)
            {
                if (method.OriginalMethod != null
                    && method.OriginalMethod.Name == nameof(HotReloadE2EFixture.ComputeWithPrivate))
                {
                    shimMethod = method.ShimMethod;
                    break;
                }
            }

            Assert.That(shimMethod, Is.Not.Null);
            MethodBody shimBody = shimMethod.GetMethodBody();
            Assert.That(shimBody, Is.Not.Null);

            IReadOnlyList<LocalBuilder> locals =
                HotReloadPausePointCoordination.GetTransplantLocals?.Invoke(computeMethod);
            Assert.That(locals, Is.Not.Null);
            Assert.That(locals.Count, Is.EqualTo(shimBody.LocalVariables.Count));
        }

        private static async Task PatchComputeWithPrivateAsync(int extraDelta = 100)
        {
            string fixturePath = Path.GetFullPath(
                Path.Combine(
                    Application.dataPath,
                    "Tests",
                    "Editor",
                    "HotReload",
                    "HotReloadE2EFixtures.cs"));
            Assert.That(File.Exists(fixturePath), Is.True);

            string onDisk = File.ReadAllText(fixturePath);
            string editedSource = onDisk.Replace(
                "return _secret + delta;",
                "return _secret + delta + " + extraDelta + ";",
                StringComparison.Ordinal);
            Assert.That(
                editedSource,
                Is.Not.EqualTo(onDisk),
                "Precondition: ComputeWithPrivate body must differ from on-disk fixture.");

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string directory = Path.Combine(
                projectRoot,
                HotReloadConstants.TestSourcesRelativeDirectory);
            Directory.CreateDirectory(directory);
            string editedPath = Path.Combine(directory, "ShimRegistryCompute_" + extraDelta + ".cs");
            File.WriteAllText(editedPath, editedSource);

            HotReloadOrchestratorResult result = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                editedPath,
                CancellationToken.None);

            bool foundPatched = false;
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                if (outcome.Kind == HotReloadMethodOutcomeKind.Patched
                    && outcome.Method.Contains(nameof(HotReloadE2EFixture.ComputeWithPrivate)))
                {
                    foundPatched = true;
                }
            }

            Assert.That(
                foundPatched,
                Is.True,
                "Expected ComputeWithPrivate to patch.\n" + FormatOutcomes(result));
        }

        private static string FormatOutcomes(HotReloadOrchestratorResult result)
        {
            List<string> lines = new List<string>();
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                lines.Add(outcome.Kind + " " + outcome.Method + " :: " + outcome.Reason);
            }

            return string.Join("\n", lines);
        }
    }
}
