using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// End-to-end coverage for the warning that an added field with a serialization attribute
    /// stays out of the Inspector: it names only fields the run really left active, and it is
    /// said once per field rather than on every reload.
    /// </summary>
    public class HotReloadAddedFieldSerializeWarningTests
    {
        private const string SerializedDeclaration = "[UnityEngine.SerializeField] public int AddedCount;";

        private const string PlainDeclaration = "public int AddedCount;";

        private const string SerializeWarningFragment = "serialization attribute";

        private const string ReadAddedOriginal =
            "        public int ReadAdded()\n        {\n            return 0;\n        }\n\n"
            + "        [MethodImpl(MethodImplOptions.NoInlining)]\n"
            + "        public void WriteAdded(int value)\n        {\n        }";

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
        }

        /// <summary>
        /// What: the first run that applies a serialized added field says so in one line that
        /// names the declaring type with the field and the recipe file to follow instead.
        /// </summary>
        [Test]
        public async Task Run_SerializedAddedFieldApplied_WarnsNamingTheDeclaringTypeAndRecipe()
        {
            HotReloadOrchestratorResult result = await RunWithDeclarationAsync(
                SerializedDeclaration,
                "SerializeWarningFirst.cs");
            AssertHasSerializeWarning(result);

            // Spelled out so a wording change has to update the pin on purpose.
            Assert.That(
                result.Warnings,
                Has.Member(
                    "Added field(s) with a serialization attribute will not appear in the Inspector "
                    + "or serialize until 'uloop compile': "
                    + typeof(HotReloadAddedFieldApplyFixture).FullName + ".AddedCount. "
                    + "To put a value in one now, follow references/added-field-wiring.md in the "
                    + "uloop-hot-reload skill."));
        }

        /// <summary>
        /// What: the result carries the serialized added fields the warning named, so the response
        /// can tell it names fields to wire; a run that names none carries none.
        /// </summary>
        [Test]
        public async Task Run_SerializedAddedFieldApplied_ResultCarriesTheFieldsTheWarningNamed()
        {
            HotReloadOrchestratorResult first = await RunWithDeclarationAsync(
                SerializedDeclaration,
                "SerializeWarningReportedFirst.cs");
            HotReloadOrchestratorResult second = await RunWithDeclarationAsync(
                SerializedDeclaration,
                "SerializeWarningReportedSecond.cs");

            Assert.That(
                first.SerializedAddedFieldsReported,
                Is.EqualTo(new[] { typeof(HotReloadAddedFieldApplyFixture).FullName + ".AddedCount" }));
            Assert.That(second.SerializedAddedFieldsReported, Is.Empty);
        }

        /// <summary>
        /// What: a file that fails to apply leaves its serialized added field out of the warning,
        /// because the field never became active.
        /// </summary>
        [Test]
        public async Task Run_SerializedAddedFieldInFailedFile_DoesNotWarn()
        {
            string fixturePath = ResolveFixturePath();
            string failing = File.ReadAllText(fixturePath).Replace(
                ReadAddedOriginal,
                "        " + SerializedDeclaration + "\n\n"
                + "        [MethodImpl(MethodImplOptions.NoInlining)]\n"
                + "        public int ReadAdded()\n        {\n            return AddedCount + MissingHelperAddedByEdit(0);\n        }\n\n"
                + "        [MethodImpl(MethodImplOptions.NoInlining)]\n"
                + "        public void WriteAdded(int value)\n        {\n            AddedCount = value;\n        }",
                StringComparison.Ordinal);

            HotReloadOrchestratorResult result = await HotReloadCompositionRoot.Services.Orchestrator.RunAsync(
                new[] { fixturePath },
                HotReloadTestSourceWriter.WriteEditedSource("SerializeWarningFailed.cs", failing),
                CancellationToken.None);

            Assert.That(result.AddedFields, Is.Empty, "The failing file must not leave the field active.");
            AssertNoSerializeWarning(result);
        }

        /// <summary>
        /// What: reapplying the same serialized added field does not repeat the warning the
        /// first run already gave.
        /// </summary>
        [Test]
        public async Task Run_SerializedAddedFieldReapplied_DoesNotRepeatTheWarning()
        {
            HotReloadOrchestratorResult first = await RunWithDeclarationAsync(
                SerializedDeclaration,
                "SerializeWarningRepeatFirst.cs");
            AssertHasSerializeWarning(first);

            HotReloadOrchestratorResult second = await RunWithDeclarationAsync(
                SerializedDeclaration,
                "SerializeWarningRepeatSecond.cs");

            AssertNoSerializeWarning(second);
        }

        /// <summary>
        /// What: after --revert-all drops the field, applying it again warns again, because the
        /// field is new to the Editor once more.
        /// </summary>
        [Test]
        public async Task Run_SerializedAddedFieldAfterRevertAll_WarnsAgain()
        {
            HotReloadOrchestratorResult first = await RunWithDeclarationAsync(
                SerializedDeclaration,
                "SerializeWarningRevertFirst.cs");
            AssertHasSerializeWarning(first);

            HotReloadCompositionRoot.Services.Patcher.RevertAll();
            HotReloadOrchestratorResult second = await RunWithDeclarationAsync(
                SerializedDeclaration,
                "SerializeWarningRevertSecond.cs");

            AssertHasSerializeWarning(second);
        }

        /// <summary>
        /// What: a run that leaves the field active without its serialization attribute ends the
        /// record, so adding the attribute back warns again.
        /// </summary>
        [Test]
        public async Task Run_SerializationAttributeDroppedThenRestored_WarnsAgain()
        {
            HotReloadOrchestratorResult first = await RunWithDeclarationAsync(
                SerializedDeclaration,
                "SerializeWarningToggleFirst.cs");
            AssertHasSerializeWarning(first);

            HotReloadOrchestratorResult plain = await RunWithDeclarationAsync(
                PlainDeclaration,
                "SerializeWarningTogglePlain.cs");
            Assert.That(plain.AddedFields, Is.Not.Empty, "The plain field must stay active.");
            AssertNoSerializeWarning(plain);

            HotReloadOrchestratorResult restored = await RunWithDeclarationAsync(
                SerializedDeclaration,
                "SerializeWarningToggleRestored.cs");

            AssertHasSerializeWarning(restored);
        }

        private static async Task<HotReloadOrchestratorResult> RunWithDeclarationAsync(
            string declaration,
            string editedFileName)
        {
            string fixturePath = ResolveFixturePath();
            string edited = File.ReadAllText(fixturePath).Replace(
                ReadAddedOriginal,
                "        " + declaration + "\n\n"
                + "        [MethodImpl(MethodImplOptions.NoInlining)]\n"
                + "        public int ReadAdded()\n        {\n            return AddedCount;\n        }\n\n"
                + "        [MethodImpl(MethodImplOptions.NoInlining)]\n"
                + "        public void WriteAdded(int value)\n        {\n            AddedCount = value;\n        }",
                StringComparison.Ordinal);
            HotReloadOrchestratorResult result = await HotReloadCompositionRoot.Services.Orchestrator.RunAsync(
                new[] { fixturePath },
                HotReloadTestSourceWriter.WriteEditedSource(editedFileName, edited),
                CancellationToken.None);
            return result;
        }

        private static void AssertHasSerializeWarning(HotReloadOrchestratorResult result)
        {
            // The field must really be active for the warning to be the right one to give.
            Assert.That(
                result.AddedFields,
                Is.EqualTo(new[] { typeof(HotReloadAddedFieldApplyFixture).FullName + ".AddedCount" }),
                string.Join("\n", result.Warnings));
            Assert.That(
                CountSerializeWarnings(result),
                Is.EqualTo(1),
                "Expected one serialization-attribute warning.\n" + string.Join("\n", result.Warnings));
        }

        private static void AssertNoSerializeWarning(HotReloadOrchestratorResult result)
        {
            Assert.That(
                CountSerializeWarnings(result),
                Is.EqualTo(0),
                "Expected no serialization-attribute warning.\n" + string.Join("\n", result.Warnings));
        }

        private static int CountSerializeWarnings(HotReloadOrchestratorResult result)
        {
            int count = 0;
            foreach (string warning in result.Warnings)
            {
                if (warning != null && warning.Contains(SerializeWarningFragment))
                {
                    count++;
                }
            }

            return count;
        }

        private static string ResolveFixturePath()
        {
            string path = Path.Combine(
                Application.dataPath,
                "Tests",
                "Editor",
                "HotReload",
                "HotReloadAddedFieldApplyFixture.cs");
            Assert.That(File.Exists(path), Is.True, "Added-field apply fixture source missing: " + path);
            return Path.GetFullPath(path);
        }
    }
}
