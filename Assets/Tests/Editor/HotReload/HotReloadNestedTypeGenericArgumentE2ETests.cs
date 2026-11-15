using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;

using Fixture = io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload.HotReloadNestedTypeGenericArgumentFixture;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// End-to-end EditMode coverage for edited methods that name a type nested in their own class
    /// as a generic type argument by its simple name, which the patched copy has to qualify because
    /// it lives outside that class.
    /// </summary>
    public class HotReloadNestedTypeGenericArgumentE2ETests
    {
        private const string FixtureFileName = "HotReloadNestedTypeGenericArgumentFixture.cs";

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
        /// What: a body that declares and constructs List&lt;Mode&gt; is patched, returns the edited
        /// value, and RevertAll restores the compiled one.
        /// </summary>
        [Test]
        public async Task Run_BodyNamingNestedTypeAsListArgument_IsPatchedAndReverts()
        {
            Fixture fixture = new Fixture();
            await AssertPatchedAndRevertedAsync(
                "BodyList", "+ 7100;", "+ 7110;", () => fixture.BodyList(), 7101, 7111);
        }

        /// <summary>
        /// What: a method whose parameter is List&lt;Mode&gt; and whose return type is
        /// Dictionary&lt;string, Item&gt; is patched, returns the edited value, and reverts.
        /// </summary>
        [Test]
        public async Task Run_SignatureNamingNestedTypesAsArguments_IsPatchedAndReverts()
        {
            Fixture fixture = new Fixture();
            await AssertPatchedAndRevertedAsync(
                "SignatureGeneric",
                "modes.Count + 7200",
                "modes.Count + 7210",
                () => fixture.SignatureGeneric(new List<Fixture.Mode> { Fixture.Mode.On })["k"].Value,
                7201,
                7211);
        }

        /// <summary>
        /// What: a body calling generic methods with nested type arguments, Pick&lt;Mode&gt;() on its
        /// own class and Enumerable.Empty&lt;Item&gt;(), is patched, returns the edited value, and reverts.
        /// </summary>
        [Test]
        public async Task Run_GenericMethodCallsWithNestedTypeArguments_ArePatchedAndRevert()
        {
            Fixture fixture = new Fixture();
            await AssertPatchedAndRevertedAsync(
                "GenericMethodArgument", "empty + 7300;", "empty + 7310;", () => fixture.GenericMethodArgument(), 7300, 7310);
        }

        /// <summary>
        /// What: typeof, a cast, and an is-test naming List&lt;Mode&gt; are patched, return the edited
        /// value, and revert.
        /// </summary>
        [Test]
        public async Task Run_TypeofCastAndIsWithNestedTypeArgument_ArePatchedAndRevert()
        {
            Fixture fixture = new Fixture();
            await AssertPatchedAndRevertedAsync(
                "TypeTestsAndCast", "? 1 : 0) + 7400;", "? 1 : 0) + 7410;", () => fixture.TypeTestsAndCast(), 7403, 7413);
        }

        /// <summary>
        /// What: the getter of a List&lt;Mode&gt; property is patched (the shim's return type names the
        /// argument qualified), returns the edited list, and reverts.
        /// </summary>
        [Test]
        public async Task Run_GetterOfNestedTypeListProperty_IsPatchedAndReverts()
        {
            Fixture fixture = new Fixture();
            await AssertPatchedAndRevertedAsync(
                "get_ModeList",
                "get { return new List<Mode> { Mode.On }; }",
                "get { return new List<Mode> { Mode.On, Mode.Off }; }",
                () => fixture.ModeList.Count,
                1,
                2);
        }

        /// <summary>
        /// What: the setter of a List&lt;Mode&gt; property is patched (the shim's value parameter names
        /// the argument qualified), the next assignment runs the edited body, and it reverts.
        /// </summary>
        [Test]
        public async Task Run_SetterOfNestedTypeListProperty_IsPatchedAndReverts()
        {
            Fixture fixture = new Fixture();
            await AssertPatchedAndRevertedAsync(
                "set_ModeList",
                "value.Count + 7500",
                "value.Count + 7510",
                () =>
                {
                    fixture.ModeList = new List<Fixture.Mode> { Fixture.Mode.On };
                    return Fixture.LastCount;
                },
                7501,
                7511);
        }

        /// <summary>
        /// What: a lambda returning a new List&lt;Mode&gt; inside an edited body is patched, returns the
        /// edited value, and reverts.
        /// </summary>
        [Test]
        public async Task Run_LambdaNamingNestedTypeAsArgument_IsPatchedAndReverts()
        {
            Fixture fixture = new Fixture();
            await AssertPatchedAndRevertedAsync(
                "LambdaGeneric", "Count + 7600;", "Count + 7610;", () => fixture.LambdaGeneric(), 7601, 7611);
        }

        /// <summary>
        /// What: control shapes that already resolved before the generic-argument fix (Mode[], Mode?,
        /// a tuple, and List&lt;Host.Mode&gt; written qualified) stay patched and revert.
        /// </summary>
        [Test]
        public async Task Run_ArrayNullableTupleAndQualifiedArgument_StayPatchedAndRevert()
        {
            Fixture fixture = new Fixture();
            await AssertPatchedAndRevertedAsync(
                "Controls", "qualified.Count + 7700;", "qualified.Count + 7710;", () => fixture.Controls(), 7703, 7713);
        }

        /// <summary>
        /// What: a nested generic type with a nested type argument (Box&lt;Mode&gt;) is patched,
        /// returns the edited value, and reverts; its qualified spelling must keep the argument.
        /// </summary>
        [Test]
        public async Task Run_NestedGenericTypeWithNestedArgument_IsPatchedAndReverts()
        {
            Fixture fixture = new Fixture();
            await AssertPatchedAndRevertedAsync(
                "NestedGenericBox", "box.Content + 7800;", "box.Content + 7810;", () => fixture.NestedGenericBox(), 7801, 7811);
        }

        private static async Task AssertPatchedAndRevertedAsync(
            string methodName,
            string anchor,
            string replacement,
            Func<int> probe,
            int compiledValue,
            int editedValue)
        {
            Assert.That(probe(), Is.EqualTo(compiledValue), "Precondition: compiled value.");

            HotReloadOrchestratorResult result = await RunEditedAsync(methodName, anchor, replacement);

            AssertPatched(result, methodName);
            Assert.That(probe(), Is.EqualTo(editedValue), "The patched body must run.");

            HotReloadCompositionRoot.Services.Patcher.RevertAll();
            Assert.That(probe(), Is.EqualTo(compiledValue), "RevertAll must restore the compiled body.");
        }

        private static async Task<HotReloadOrchestratorResult> RunEditedAsync(
            string methodName,
            string anchor,
            string replacement)
        {
            string fixturePath = FixturePath();
            string source = File.ReadAllText(fixturePath);
            Assert.That(
                source.IndexOf(anchor, StringComparison.Ordinal),
                Is.EqualTo(source.LastIndexOf(anchor, StringComparison.Ordinal)).And.GreaterThanOrEqualTo(0),
                "Precondition: the fixture must hold the anchor exactly once: " + anchor);
            string edited = source.Replace(anchor, replacement, StringComparison.Ordinal);
            return await HotReloadCompositionRoot.Services.Orchestrator.RunAsync(
                new[] { fixturePath },
                contentPathOverride: null,
                CancellationToken.None,
                new Dictionary<string, string>
                {
                    [fixturePath] = HotReloadTestSourceWriter.WriteEditedSource(
                        "NestedTypeGenericArgument_" + methodName + ".cs",
                        edited)
                });
        }

        private static string FixturePath()
        {
            string path = Path.GetFullPath(
                Path.Combine(Application.dataPath, "Tests", "Editor", "HotReload", FixtureFileName));
            Assert.That(File.Exists(path), Is.True, "Fixture missing: " + path);
            return path;
        }

        private static void AssertPatched(HotReloadOrchestratorResult result, string methodName)
        {
            string marker = "." + methodName + "(";
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                if (outcome.Kind == HotReloadMethodOutcomeKind.Patched
                    && outcome.Method != null
                    && outcome.Method.Contains(marker))
                {
                    return;
                }
            }

            Assert.Fail("Expected Patched for " + methodName + ".\n" + FormatOutcomes(result));
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
