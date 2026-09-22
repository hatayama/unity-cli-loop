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
    /// End-to-end EditMode coverage for added array fields and properties whose initializer is the
    /// short array form '= { ... }', which is only valid C# beside an array declaration.
    /// </summary>
    public class HotReloadAddedFieldArrayInitializerE2ETests
    {
        private const string FixtureFileName = "HotReloadAddedFieldApplyFixture.cs";
        private const string ReadAddedOriginal =
            "        public int ReadAdded()\n        {\n            return 0;\n        }";

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
        /// What: an added static readonly array field initialized with '= { 1, 2 }' applies and the
        /// patched reader iterates the initializer's values.
        /// </summary>
        [Test]
        public async Task Run_AddedStaticArrayFieldWithShortInitializer_PatchedReaderSeesTheValues()
        {
            await AssertReadAddedReturns(
                "private static readonly int[] AddedTable = { 1, 2 };",
                "int sum = 0;\n            foreach (int value in AddedTable)\n            {\n"
                + "                sum += value;\n            }\n\n            return sum;",
                3);
        }

        /// <summary>
        /// What: an added instance array field initialized with '= { 4 }' applies and the patched
        /// reader sees the initializer's element on a fresh instance.
        /// </summary>
        [Test]
        public async Task Run_AddedInstanceArrayFieldWithShortInitializer_PatchedReaderSeesTheValue()
        {
            await AssertReadAddedReturns("private int[] _addedSlots = { 4 };", "return _addedSlots[0];", 4);
        }

        /// <summary>
        /// What: a jagged array field's short initializer applies with its inner array creations.
        /// </summary>
        [Test]
        public async Task Run_AddedJaggedArrayFieldWithShortInitializer_PatchedReaderSeesTheValue()
        {
            await AssertReadAddedReturns(
                "private static readonly int[][] AddedJagged = { new[] { 1 }, new[] { 2, 3 } };",
                "return AddedJagged[1][1];",
                3);
        }

        /// <summary>
        /// What: a rectangular array field's short initializer applies with its nested rows, which
        /// the wrapping array creation must carry the rank of.
        /// </summary>
        [Test]
        public async Task Run_AddedRectangularArrayFieldWithShortInitializer_PatchedReaderSeesTheValue()
        {
            await AssertReadAddedReturns(
                "private static readonly int[,] AddedGrid = { { 1, 2 }, { 3, 4 } };",
                "return AddedGrid[1, 0];",
                3);
        }

        /// <summary>
        /// What: an added auto-property initialized with '= { 5 }' applies too, since its store is
        /// initialized through the same lambda as an added field's.
        /// </summary>
        [Test]
        public async Task Run_AddedArrayAutoPropertyWithShortInitializer_PatchedReaderSeesTheValue()
        {
            await AssertReadAddedReturns(
                "public static int[] AddedRow { get; } = { 5 };",
                "return AddedRow[0];",
                5);
        }

        /// <summary>
        /// What: reloading the same short initializer again does not warn that the initializer
        /// changed, because the text recorded for the comparison is the one the source spells and
        /// not the array creation the shim emits.
        /// </summary>
        [Test]
        public async Task Run_SameShortInitializerReloadedAgain_DoesNotWarnThatTheInitializerChanged()
        {
            string edited = WithAddedMember("private static readonly int[] AddedTable = { 1, 2 };", "return AddedTable[1];");
            await RunAsync(edited);

            HotReloadOrchestratorResult second = await RunAsync(edited);

            Assert.That(new HotReloadAddedFieldApplyFixture().ReadAdded(), Is.EqualTo(2), FormatOutcomes(second));
            string changedWarningStart = HotReloadConstants.AddedFieldInitializerChangedWarningFormat.Substring(0, 40);
            Assert.That(
                second.Warnings ?? new List<string>(),
                Has.None.Contains(changedWarningStart),
                FormatOutcomes(second));
        }

        private static async Task AssertReadAddedReturns(string addedMember, string readAddedBody, int expected)
        {
            HotReloadOrchestratorResult result = await RunAsync(WithAddedMember(addedMember, readAddedBody));

            HotReloadMethodOutcome readAdded = FindOutcome(result, ".ReadAdded(");
            Assert.That(readAdded, Is.Not.Null, FormatOutcomes(result));
            Assert.That(readAdded.Kind, Is.EqualTo(HotReloadMethodOutcomeKind.Patched), FormatOutcomes(result));
            Assert.That(new HotReloadAddedFieldApplyFixture().ReadAdded(), Is.EqualTo(expected));
        }

        private static string WithAddedMember(string addedMember, string readAddedBody)
        {
            string onDisk = File.ReadAllText(FixturePath());
            Assert.That(onDisk, Does.Contain(ReadAddedOriginal), "Precondition: ReadAdded anchor must exist.");
            return onDisk.Replace(
                ReadAddedOriginal,
                "        " + addedMember + "\n\n"
                + "        public int ReadAdded()\n        {\n            " + readAddedBody + "\n        }");
        }

        private static Task<HotReloadOrchestratorResult> RunAsync(string edited)
        {
            return HotReloadCompositionRoot.Services.Orchestrator.RunAsync(
                new[] { FixturePath() },
                HotReloadTestSourceWriter.WriteEditedSource("AddedFieldArrayInitializerE2E.cs", edited),
                CancellationToken.None);
        }

        private static HotReloadMethodOutcome FindOutcome(HotReloadOrchestratorResult result, string methodPart)
        {
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                if (outcome.Method != null && outcome.Method.Contains(methodPart))
                {
                    return outcome;
                }
            }

            return null;
        }

        private static string FixturePath()
        {
            string path = Path.GetFullPath(
                Path.Combine(Application.dataPath, "Tests", "Editor", "HotReload", FixtureFileName));
            Assert.That(File.Exists(path), Is.True, "Fixture missing: " + path);
            return path;
        }

        private static string FormatOutcomes(HotReloadOrchestratorResult result)
        {
            List<string> lines = new List<string>();
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                lines.Add(outcome.Kind + " " + outcome.Method + " @" + outcome.FilePath + " :: " + outcome.Reason);
            }

            lines.AddRange(result.Warnings ?? new List<string>());
            return string.Join("\n", lines);
        }
    }
}
