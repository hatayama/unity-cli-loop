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
    /// End-to-end EditMode coverage for edited bodies that name a private nested type of their
    /// own type, which the patched copy has to reach from outside that type.
    /// </summary>
    public class HotReloadNestedTypeShimE2ETests
    {
        private const string HostFileName = "HotReloadNestedTypeHost.cs";
        private const string SumBodyAnchor = "Cell cell = new Cell(1);\n            return cell.Value;";
        private const string MakeBodyAnchor = "return Cell.Of(value);";

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
        /// What: bodies that name a private nested type by its simple name, in a local, a
        /// constructor call, a static call, and a signature, are patched and return the edited
        /// values instead of failing the file.
        /// </summary>
        [Test]
        public async Task Run_EditedBodiesNamingANestedTypeBySimpleName_ArePatched()
        {
            string edited = ReplaceInSource(
                ReplaceInSource(
                    ReadFixture(HostFileName),
                    SumBodyAnchor,
                    "Cell a = Cell.Of(2);\n            Cell b = new Cell(3);\n"
                    + "            return a.Value + b.Value + Make(4).Value;"),
                MakeBodyAnchor,
                "return new Cell(value + 1);");

            HotReloadOrchestratorResult result = await RunAsync(edited, "NestedTypeSimpleName.cs");

            AssertNoFailure(result);
            FindOutcome(result, HotReloadMethodOutcomeKind.Patched, "Sum");
            FindOutcome(result, HotReloadMethodOutcomeKind.Patched, "Make");
            Assert.That(new HotReloadNestedTypeHost().Sum(), Is.EqualTo(10));
        }

        /// <summary>
        /// What: a body that already qualifies the nested type with its declaring type is patched
        /// as written, without the qualifier being spelled out twice.
        /// </summary>
        [Test]
        public async Task Run_EditedBodyNamingANestedTypeQualified_IsPatched()
        {
            string edited = ReplaceInSource(
                ReadFixture(HostFileName),
                SumBodyAnchor,
                "HotReloadNestedTypeHost.Cell cell = HotReloadNestedTypeHost.Cell.Of(6);\n"
                + "            return cell.Value;");

            HotReloadOrchestratorResult result = await RunAsync(edited, "NestedTypeQualifiedName.cs");

            AssertNoFailure(result);
            FindOutcome(result, HotReloadMethodOutcomeKind.Patched, "Sum");
            Assert.That(new HotReloadNestedTypeHost().Sum(), Is.EqualTo(6));
        }

        private static async Task<HotReloadOrchestratorResult> RunAsync(string editedSource, string editedFileName)
        {
            string hostPath = FixturePath(HostFileName);
            return await HotReloadCompositionRoot.Services.Orchestrator.RunAsync(
                new[] { hostPath },
                contentPathOverride: null,
                CancellationToken.None,
                new Dictionary<string, string>
                {
                    [hostPath] = HotReloadTestSourceWriter.WriteEditedSource(editedFileName, editedSource)
                });
        }

        private static string ReplaceInSource(string source, string anchor, string replacement)
        {
            Assert.That(source, Does.Contain(anchor), "Precondition: anchor must exist: " + anchor);
            return source.Replace(anchor, replacement, StringComparison.Ordinal);
        }

        private static string ReadFixture(string fileName)
        {
            // Why normalized: the anchors span lines, and a checkout with CRLF endings would
            // otherwise never match them.
            return File.ReadAllText(FixturePath(fileName)).Replace("\r\n", "\n");
        }

        private static string FixturePath(string fileName)
        {
            string path = Path.GetFullPath(
                Path.Combine(Application.dataPath, "Tests", "Editor", "HotReload", fileName));
            Assert.That(File.Exists(path), Is.True, "Fixture missing: " + path);
            return path;
        }

        private static void AssertNoFailure(HotReloadOrchestratorResult result)
        {
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                Assert.That(
                    outcome.Kind,
                    Is.Not.EqualTo(HotReloadMethodOutcomeKind.Failed),
                    "Unexpected failure.\n" + FormatOutcomes(result));
            }
        }

        private static HotReloadMethodOutcome FindOutcome(
            HotReloadOrchestratorResult result,
            HotReloadMethodOutcomeKind kind,
            string methodNamePart)
        {
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                if (outcome.Kind == kind && outcome.Method != null && outcome.Method.Contains(methodNamePart))
                {
                    return outcome;
                }
            }

            Assert.Fail("Expected " + kind + " for " + methodNamePart + ".\n" + FormatOutcomes(result));
            return null;
        }

        private static string FormatOutcomes(HotReloadOrchestratorResult result)
        {
            List<string> lines = new List<string>();
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                lines.Add(outcome.Kind + " " + outcome.Method + " @" + outcome.FilePath + " :: " + outcome.Reason);
            }

            return string.Join("\n", lines);
        }
    }
}
