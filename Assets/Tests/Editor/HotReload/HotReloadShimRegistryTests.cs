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
        /// patched method for the fixture's project-relative path.
        /// </summary>
        [Test]
        public async Task Apply_ThenGetShimLookupForFile_ReturnsPatchedMethods()
        {
            await PatchComputeWithPrivateAsync();

            HotReloadShimFileLookup lookup =
                HotReloadPausePointCoordination.GetShimLookupForFile?.Invoke(FixtureProjectRelativePath);
            Assert.That(lookup, Is.Not.Null);
            Assert.That(lookup.AssemblyBytes, Is.Not.Null.And.Not.Empty);
            Assert.That(lookup.LoadedAssembly, Is.Not.Null);
            Assert.That(lookup.Methods, Is.Not.Empty);

            bool foundCompute = false;
            foreach (HotReloadShimMethodLookup method in lookup.Methods)
            {
                if (method.OriginalMethod != null
                    && method.OriginalMethod.Name == nameof(HotReloadE2EFixture.ComputeWithPrivate))
                {
                    foundCompute = true;
                    Assert.That(method.ShimMethod, Is.Not.Null);
                    Assert.That(method.SourceStartLine, Is.GreaterThan(0));
                    Assert.That(method.SourceEndLine, Is.GreaterThanOrEqualTo(method.SourceStartLine));
                }
            }

            Assert.That(foundCompute, Is.True, "ComputeWithPrivate missing from shim lookup Methods.");
        }

        /// <summary>
        /// What: reverting a patched method removes it from GetShimLookupForFile (lookup becomes
        /// null when it was the last registered method for that file).
        /// </summary>
        [Test]
        public async Task Revert_RemovesMethodFromShimLookup()
        {
            await PatchComputeWithPrivateAsync();
            MethodInfo computeMethod = typeof(HotReloadE2EFixture).GetMethod(
                nameof(HotReloadE2EFixture.ComputeWithPrivate),
                BindingFlags.Instance | BindingFlags.Public);
            Assert.That(computeMethod, Is.Not.Null);

            bool reverted = HotReloadPatcher.Revert(computeMethod);
            Assert.That(reverted, Is.True);

            HotReloadShimFileLookup lookup =
                HotReloadPausePointCoordination.GetShimLookupForFile?.Invoke(FixtureProjectRelativePath);
            if (lookup == null)
            {
                return;
            }

            // Why allow non-null lookup: editing `return _secret + delta;` can also touch sibling
            // methods that share that statement text; only ComputeWithPrivate must disappear.
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
        /// What: GetTransplantLocals returns a non-null list for a method patched via transplant.
        /// </summary>
        [Test]
        public async Task Apply_Transplant_ExposesTransplantLocals()
        {
            await PatchComputeWithPrivateAsync();
            MethodInfo computeMethod = typeof(HotReloadE2EFixture).GetMethod(
                nameof(HotReloadE2EFixture.ComputeWithPrivate),
                BindingFlags.Instance | BindingFlags.Public);
            Assert.That(computeMethod, Is.Not.Null);

            IReadOnlyList<LocalBuilder> locals =
                HotReloadPausePointCoordination.GetTransplantLocals?.Invoke(computeMethod);
            Assert.That(locals, Is.Not.Null);
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
