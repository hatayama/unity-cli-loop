using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// End-to-end EditMode coverage for edited members that need an unsafe context: such a member is
    /// Skipped with its own row while a safe method edited in the same file is still patched, and a
    /// safe method inside an unsafe class is patched. The fixture lives in an assembly that allows
    /// unsafe code, which this test assembly does not, so it is reached by reflection.
    /// </summary>
    public class HotReloadUnsafeCodeE2ETests
    {
        private const string FixtureAssemblyName = "UnityCLILoop.Tests.Editor.HotReloadUnsafeFixture";
        private const string FixtureNamespace = "io.github.hatayama.UnityCliLoop.Tests.Editor.HotReloadUnsafeFixture.";
        private const string UnsafeCodeReason = "need an unsafe context";

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
            HotReloadCompositionRoot.Services.Patcher.RevertAll();
            _scope.Dispose();
            HotReloadAutoRefreshHold.SyncToActiveChanges();
            VibeLogger.ClearMemoryLogs();
        }

        /// <summary>
        /// What: a pointer body in an unsafe class is Skipped with the unsafe-code reason and keeps its
        /// compiled body, while the safe method edited in the same file is patched.
        /// </summary>
        [Test]
        public async Task Run_PointerBodyInUnsafeClass_IsSkippedAndTheSafeMethodIsPatched()
        {
            Assert.That(Read("UnsafeFixtureClassPointerHost"), Is.EqualTo(1303), "Precondition: compiled body.");

            HotReloadOrchestratorResult result = await RunEditedAsync("UnsafeClassPointerE2E.cs", "*p + 1300", "*p + 1310");

            AssertSkippedAsUnsafe(result, "UnsafeFixtureClassPointerHost.Read(");
            AssertSafeMethodPatched(result);
            Assert.That(Read("UnsafeFixtureClassPointerHost"), Is.EqualTo(1303), "The skipped member must keep its compiled body.");
        }

        /// <summary>
        /// What: an edited getter of a pointer-typed property gets its own Skipped row (not a
        /// shim-compile failure), while the safe method edited in the same file is patched.
        /// </summary>
        [Test]
        public async Task Run_PointerTypedPropertyGetter_IsSkippedAndTheSafeMethodIsPatched()
        {
            HotReloadOrchestratorResult result = await RunEditedAsync("PointerTypedPropertyE2E.cs", "(IntPtr)1650", "(IntPtr)1660");

            AssertSkippedAsUnsafe(result, "UnsafeFixturePointerTypedPropertyHost.get_Pointer(");
            AssertSafeMethodPatched(result);
        }

        /// <summary>
        /// What: a safe method inside an unsafe class is patched, returns the edited value, and returns
        /// the compiled value after RevertAll.
        /// </summary>
        [Test]
        public async Task Run_SafeMethodInUnsafeClass_IsPatched()
        {
            Assert.That(Read("UnsafeFixtureClassSafeHost"), Is.EqualTo(1350), "Precondition: compiled body.");

            HotReloadOrchestratorResult result = await RunEditedAsync("UnsafeClassSafeE2E.cs", "return 1350;", "return 1360;");

            AssertPatched(result, "UnsafeFixtureClassSafeHost.Read(");
            AssertSafeMethodPatched(result);
            Assert.That(Read("UnsafeFixtureClassSafeHost"), Is.EqualTo(1360), "The patched body must run.");

            HotReloadCompositionRoot.Services.Patcher.RevertAll();
            Assert.That(Read("UnsafeFixtureClassSafeHost"), Is.EqualTo(1350), "RevertAll must restore the compiled body.");
        }

        // Edits the anchor and the safe method together, and applies the edit as a content override so
        // the fixture file on disk is never written.
        private static async Task<HotReloadOrchestratorResult> RunEditedAsync(
            string editedFileName,
            string anchor,
            string replacement)
        {
            string fixturePath = Path.GetFullPath(
                Path.Combine(Application.dataPath, "Tests", "Editor", "HotReloadUnsafeFixture", "HotReloadUnsafeFixture.cs"));
            string onDisk = File.ReadAllText(fixturePath);
            string edited = ReplaceUnique(ReplaceUnique(onDisk, anchor, replacement), "return 1000;", "return 1001;");
            HotReloadOrchestratorResult result = await HotReloadCompositionRoot.Services.Orchestrator.RunAsync(
                new[] { fixturePath },
                contentPathOverride: null,
                CancellationToken.None,
                new Dictionary<string, string>
                {
                    [fixturePath] = HotReloadTestSourceWriter.WriteEditedSource(editedFileName, edited)
                });
            Assert.That(File.ReadAllText(fixturePath), Is.EqualTo(onDisk), "The fixture file must stay untouched.");
            return result;
        }

        private static string ReplaceUnique(string source, string anchor, string replacement)
        {
            Assert.That(
                source.IndexOf(anchor, StringComparison.Ordinal),
                Is.EqualTo(source.LastIndexOf(anchor, StringComparison.Ordinal)).And.GreaterThanOrEqualTo(0),
                "Precondition: the fixture must hold the anchor exactly once: " + anchor);
            return source.Replace(anchor, replacement, StringComparison.Ordinal);
        }

        private static int Read(string typeName)
        {
            Type type = Assembly.Load(FixtureAssemblyName).GetType(FixtureNamespace + typeName, true);
            object instance = Activator.CreateInstance(type);
            MethodInfo read = type.GetMethod("Read", BindingFlags.Public | BindingFlags.Instance);
            return (int)read.Invoke(instance, null);
        }

        private static void AssertSafeMethodPatched(HotReloadOrchestratorResult result)
        {
            AssertPatched(result, "UnsafeFixtureSafeHost.Read(");
            Assert.That(Read("UnsafeFixtureSafeHost"), Is.EqualTo(1001), "The safe method in the same file must be patched.");
        }

        private static void AssertPatched(HotReloadOrchestratorResult result, string methodFragment)
        {
            HotReloadMethodOutcome outcome = FindSingle(result, methodFragment);
            Assert.That(outcome.Kind, Is.EqualTo(HotReloadMethodOutcomeKind.Patched), FormatOutcomes(result));
        }

        private static void AssertSkippedAsUnsafe(HotReloadOrchestratorResult result, string methodFragment)
        {
            HotReloadMethodOutcome outcome = FindSingle(result, methodFragment);
            Assert.That(outcome.Kind, Is.EqualTo(HotReloadMethodOutcomeKind.Skipped), FormatOutcomes(result));
            Assert.That(outcome.Reason, Does.Contain(UnsafeCodeReason), FormatOutcomes(result));
            foreach (HotReloadMethodOutcome other in result.Methods)
            {
                Assert.That(other.Kind, Is.Not.EqualTo(HotReloadMethodOutcomeKind.Failed), FormatOutcomes(result));
            }
        }

        private static HotReloadMethodOutcome FindSingle(HotReloadOrchestratorResult result, string methodFragment)
        {
            List<HotReloadMethodOutcome> found = new List<HotReloadMethodOutcome>();
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                if (outcome.Method.Contains(methodFragment))
                {
                    found.Add(outcome);
                }
            }

            Assert.That(found.Count, Is.EqualTo(1), "Expected one row for " + methodFragment + ".\n" + FormatOutcomes(result));
            return found[0];
        }

        private static string FormatOutcomes(HotReloadOrchestratorResult result)
        {
            StringBuilder builder = new StringBuilder();
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                builder.Append(outcome.Kind).Append(' ').Append(outcome.Method)
                    .Append(" :: ").Append(outcome.Reason).Append('\n');
            }

            return builder.ToString();
        }
    }
}
