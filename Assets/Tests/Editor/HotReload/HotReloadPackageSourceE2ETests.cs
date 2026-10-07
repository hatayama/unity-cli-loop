using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// End-to-end EditMode coverage for an edit of a source in an embedded package, whose asset path
    /// (Packages/&lt;package-name&gt;/...) and file path (Packages/&lt;folder&gt;/...) differ: the
    /// edited method alone is patched, whichever of the two paths names the file.
    /// </summary>
    /// <remarks>
    /// This test assembly does not reference the fixture package, the same as an assembly a user
    /// edits is not referenced by the tool, so the fixture type is reached through reflection.
    /// </remarks>
    public class HotReloadPackageSourceE2ETests
    {
        private const string FixtureAssetPath =
            "Packages/io.github.hatayama.uloop.hotreload-package-fixture/Runtime/HotReloadPackageFixture.cs";
        private const string FixturePhysicalPath =
            "Packages/uloop-hotreload-package-fixture/Runtime/HotReloadPackageFixture.cs";
        private const string FixtureTypeName = "HotReloadPackageFixture";
        private const string FixtureAssemblyQualifiedTypeName =
            "io.github.hatayama.UnityCliLoop.Tests.PackageFixture." + FixtureTypeName
            + ", UnityCLILoop.Tests.HotReloadPackageFixture";
        private const string SecondCompiledBody = "return 2;";
        private const string SecondEditedBody = "return 20;";
        // Part of the warning for a file without a baseline, whatever the reason the baseline is missing.
        private const string PatchingAllMethodsWarningPart = "patching all methods";

        private HotReloadDomainTestScope _scope;

        [SetUp]
        public void SetUp()
        {
            _scope = new HotReloadDomainTestScope();
            HotReloadAutoRefreshHold.SyncToActiveChanges();
        }

        [TearDown]
        public void TearDown()
        {
            _scope.Dispose();
            HotReloadAutoRefreshHold.SyncToActiveChanges();
            VibeLogger.ClearMemoryLogs();
        }

        /// <summary>
        /// What: an edit of one method of a package source named by its asset path patches that
        /// method alone, without the missing-baseline warning, and the edited body runs.
        /// </summary>
        [Test]
        public async Task Run_EditingOneMethodOfAPackageSource_PatchesOnlyThatMethod()
        {
            HotReloadOrchestratorResult result = await RunEditOfSecondAsync(FixtureAssetPath, "PackageSourceByAssetPath.cs");

            AssertOnlySecondIsPatched(result);
            Assert.That(CallFixture("Second"), Is.EqualTo(20), FormatOutcomes(result));
            Assert.That(CallFixture("First"), Is.EqualTo(1), FormatOutcomes(result));
        }

        /// <summary>
        /// What: the same edit, with the file named by its path in the package's folder, gives the
        /// same result, since that path is mapped to the asset path before anything else.
        /// </summary>
        [Test]
        public async Task Run_EditingOneMethodOfAPackageSourceGivenByItsPhysicalPath_PatchesOnlyThatMethod()
        {
            HotReloadOrchestratorResult result = await RunEditOfSecondAsync(FixturePhysicalPath, "PackageSourceByPhysicalPath.cs");

            AssertOnlySecondIsPatched(result);
            Assert.That(CallFixture("Second"), Is.EqualTo(20), FormatOutcomes(result));
            Assert.That(CallFixture("First"), Is.EqualTo(1), FormatOutcomes(result));
        }

        // Unchanged methods get no row; they are only counted. Why at least two rather than exactly
        // two: a method the compiler adds, such as an implicit constructor, may be counted too.
        private static void AssertOnlySecondIsPatched(HotReloadOrchestratorResult result)
        {
            Assert.That(FindRow(result, "Second").Kind, Is.EqualTo(HotReloadMethodOutcomeKind.Patched), FormatOutcomes(result));
            Assert.That(CountRows(result, HotReloadMethodOutcomeKind.Patched), Is.EqualTo(1), FormatOutcomes(result));
            Assert.That(result.UnchangedTotal, Is.GreaterThanOrEqualTo(2), "First and Third are unchanged.\n" + FormatOutcomes(result));
            Assert.That(CountRows(result, HotReloadMethodOutcomeKind.Failed), Is.EqualTo(0), FormatOutcomes(result));
            foreach (string warning in result.Warnings)
            {
                Assert.That(
                    warning == null || !warning.Contains(PatchingAllMethodsWarningPart, StringComparison.Ordinal),
                    Is.True,
                    "A package source with a verified snapshot must have a baseline.\nUnexpected warning: " + warning);
            }
        }

        private static async Task<HotReloadOrchestratorResult> RunEditOfSecondAsync(string inputPath, string editedFileName)
        {
            string fixtureFilePath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", FixturePhysicalPath));
            string editedSource = ReplaceOnce(File.ReadAllText(fixtureFilePath), SecondCompiledBody, SecondEditedBody);
            string editedPath = HotReloadTestSourceWriter.WriteEditedSource(editedFileName, editedSource);
            return await HotReloadCompositionRoot.Services.Orchestrator.RunAsync(
                new[] { inputPath },
                contentPathOverride: null,
                CancellationToken.None,
                new Dictionary<string, string> { [inputPath] = editedPath });
        }

        // Why the uniqueness check: a fragment that also matched another method would edit a method
        // the test does not look at, and the asserts would pass or fail for the wrong reason.
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

        private static int CallFixture(string methodName)
        {
            Type fixtureType = Type.GetType(FixtureAssemblyQualifiedTypeName, throwOnError: true);
            object fixture = Activator.CreateInstance(fixtureType);
            MethodInfo method = fixtureType.GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance);
            Assert.That(method, Is.Not.Null, "Fixture method missing: " + methodName);
            return (int)method.Invoke(fixture, null);
        }

        // Why the type and the parenthesis: a bare method name would also match a longer name that
        // contains it.
        private static HotReloadMethodOutcome FindRow(HotReloadOrchestratorResult result, string methodName)
        {
            string labelPart = "." + FixtureTypeName + "." + methodName + "(";
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                if (outcome.Method != null && outcome.Method.Contains(labelPart))
                {
                    return outcome;
                }
            }

            Assert.Fail("No row for " + FixtureTypeName + "." + methodName + ".\n" + FormatOutcomes(result));
            return null;
        }

        private static int CountRows(HotReloadOrchestratorResult result, HotReloadMethodOutcomeKind kind)
        {
            int count = 0;
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                if (outcome.Kind == kind)
                {
                    count++;
                }
            }

            return count;
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
