using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// End-to-end EditMode coverage for an edited body whose extension method call a referenced
    /// assembly declares twice: publicly, and as an internal type or member it grants to no one.
    /// The edited assembly's own compile binds the call to the public method, and the shim compile
    /// must bind it the same way instead of failing it as ambiguous.
    /// </summary>
    public class HotReloadShimReferenceForeignInternalE2ETests
    {
        private const string FixtureFileName = "HotReloadShimReferenceForeignInternalFixture.cs";
        private const string ForeignAssemblyName = "UnityCLILoop.Tests.Editor.HotReload.ShimReferenceForeignInternal";

        private HotReloadDomainTestScope _scope;

        [SetUp]
        public void SetUp()
        {
            _scope = new HotReloadDomainTestScope();
            HotReloadAutoRefreshHold.SyncToActiveChanges();

            // Why: the foreign assembly never recompiles, so its Mvid and the copies keyed by it
            // outlive any change to the rewrite rule this class is meant to exercise.
            PublicizedCopyTestCache.DeleteCopiesOf(ForeignAssemblyName, HotReloadConstants.PublicizedRefsRelativeDirectory);
            PublicizedCopyTestCache.DeleteCopiesOf(ForeignAssemblyName, HotReloadConstants.PublicizedExternalRefsRelativeDirectory);
        }

        [TearDown]
        public void TearDown()
        {
            _scope.Dispose();
            HotReloadAutoRefreshHold.SyncToActiveChanges();
            VibeLogger.ClearMemoryLogs();
        }

        /// <summary>
        /// What: an edited body calling an extension method that a referenced assembly also declares
        /// on an internal type is patched with no failed row, and the call returns the edited value.
        /// </summary>
        [Test]
        public async Task Run_BodyCallingAnExtensionShadowedByAForeignInternalClass_PatchesWithoutAmbiguity()
        {
            HotReloadOrchestratorResult result = await RunEditAsync(
                "return value.Tripled();",
                "return value.Tripled() + 1;",
                "ShimReferenceForeignInternalTriple.cs");

            AssertPatchedWithoutFailedRows(result, "TripleViaExtension");
            Assert.That(
                new HotReloadShimReferenceForeignInternalFixture().TripleViaExtension(5),
                Is.EqualTo(16),
                FormatOutcomes(result));
        }

        /// <summary>
        /// What: the same holds when the referenced assembly declares the extension method as an
        /// internal member of a public type.
        /// </summary>
        [Test]
        public async Task Run_BodyCallingAnExtensionShadowedByAForeignInternalMethodOfAPublicClass_PatchesWithoutAmbiguity()
        {
            HotReloadOrchestratorResult result = await RunEditAsync(
                "return value.Quadrupled();",
                "return value.Quadrupled() + 1;",
                "ShimReferenceForeignInternalQuadruple.cs");

            AssertPatchedWithoutFailedRows(result, "QuadrupleViaExtension");
            Assert.That(
                new HotReloadShimReferenceForeignInternalFixture().QuadrupleViaExtension(5),
                Is.EqualTo(21),
                FormatOutcomes(result));
        }

        private static async Task<HotReloadOrchestratorResult> RunEditAsync(
            string fragment,
            string replacement,
            string editedFileName)
        {
            string path = FixturePath();
            string editedPath = HotReloadTestSourceWriter.WriteEditedSource(
                editedFileName,
                ReplaceOnce(File.ReadAllText(path), fragment, replacement));
            return await HotReloadCompositionRoot.Services.Orchestrator.RunAsync(
                new[] { path },
                contentPathOverride: null,
                CancellationToken.None,
                new Dictionary<string, string> { [path] = editedPath });
        }

        private static void AssertPatchedWithoutFailedRows(HotReloadOrchestratorResult result, string methodName)
        {
            Assert.That(FindRow(result, methodName).Kind, Is.EqualTo(HotReloadMethodOutcomeKind.Patched), FormatOutcomes(result));
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                Assert.That(outcome.Kind, Is.Not.EqualTo(HotReloadMethodOutcomeKind.Failed), FormatOutcomes(result));
            }
        }

        // Why the type and the parenthesis: a bare method name would also match a longer name that
        // contains it.
        private static HotReloadMethodOutcome FindRow(HotReloadOrchestratorResult result, string methodName)
        {
            string labelPart = "." + nameof(HotReloadShimReferenceForeignInternalFixture) + "." + methodName + "(";
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                if (outcome.Method != null && outcome.Method.Contains(labelPart))
                {
                    return outcome;
                }
            }

            Assert.Fail("No row for " + methodName + ".\n" + FormatOutcomes(result));
            return null;
        }

        // Why the uniqueness check: a fragment that also matched another member would edit a method
        // the test does not call, and the assert would pass or fail for the wrong reason.
        private static string ReplaceOnce(string source, string fragment, string replacement)
        {
            int first = source.IndexOf(fragment, StringComparison.Ordinal);
            Assert.That(first, Is.GreaterThanOrEqualTo(0), "Fragment missing from the fixture: " + fragment);
            Assert.That(
                source.IndexOf(fragment, first + fragment.Length, StringComparison.Ordinal),
                Is.EqualTo(-1),
                "Fragment occurs more than once in the fixture: " + fragment);
            return source.Replace(fragment, replacement, StringComparison.Ordinal);
        }

        private static string FixturePath()
        {
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "Tests", "Editor", "HotReload", FixtureFileName));
            Assert.That(File.Exists(path), Is.True, "Fixture missing: " + path);
            return path;
        }

        private static string FormatOutcomes(HotReloadOrchestratorResult result)
        {
            List<string> lines = new List<string>();
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                lines.Add(outcome.Kind + " " + outcome.Method + " :: " + outcome.Reason);
            }

            return string.Join("\n", lines) + "\nWarnings:\n" + string.Join("\n", result.Warnings);
        }
    }
}
