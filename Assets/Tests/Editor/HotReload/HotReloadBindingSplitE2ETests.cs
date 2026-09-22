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
    /// End-to-end EditMode coverage for the skipped row of an added method that cannot bind
    /// because a file the reload pulls in on its own declares a compiled type from source.
    /// </summary>
    public class HotReloadBindingSplitE2ETests
    {
        private const string HostFileName = "HotReloadBindingSplitHost.cs";
        private const string PayloadFileName = "HotReloadBindingSplitPayload.cs";
        private const string RegistryFileName = "HotReloadBindingSplitRegistry.cs";
        private const string HostInsertionAnchor = "        public int Handled => _handled;";
        private const string WireMethod =
            "\n\n        public void Wire()\n        {\n            _registry.Register(p => Handle(p));\n        }";
        private const string PayloadScaledBody = "return Value * 2;";

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
        /// What: when only the host is passed but the payload file comes back as an active sibling,
        /// the added method that cannot bind is skipped with a reason naming the file declaring the
        /// compiled API, because the reader never listed the payload file and has no other way to
        /// learn which file to pass.
        /// </summary>
        [Test]
        public async Task Run_PayloadReappliedAsSibling_SkippedRowNamesTheFileDeclaringTheCompiledSignature()
        {
            string hostPath = FixturePath(HostFileName);
            string payloadPath = FixturePath(PayloadFileName);
            string payloadEditPath = HotReloadTestSourceWriter.WriteEditedSource(
                "BindingSplitE2EPayload.cs",
                ReplaceOnce(File.ReadAllText(payloadPath), PayloadScaledBody, "return Value * 3;"));

            HotReloadOrchestratorResult first = await HotReloadCompositionRoot.Services.Orchestrator.RunAsync(
                new[] { payloadPath },
                contentPathOverride: null,
                CancellationToken.None,
                new Dictionary<string, string> { [payloadPath] = payloadEditPath });
            Assert.That(
                new HotReloadBindingSplitPayload { Value = 2 }.Scaled(),
                Is.EqualTo(6),
                "Precondition: the payload body must be patched.\n" + FormatOutcomes(first));

            HotReloadOrchestratorResult second = await HotReloadCompositionRoot.Services.Orchestrator.RunAsync(
                new[] { hostPath },
                contentPathOverride: null,
                CancellationToken.None,
                new Dictionary<string, string>
                {
                    [hostPath] = HotReloadTestSourceWriter.WriteEditedSource(
                        "BindingSplitE2EHost.cs",
                        ReplaceOnce(File.ReadAllText(hostPath), HostInsertionAnchor, HostInsertionAnchor + WireMethod)),
                    [payloadPath] = payloadEditPath
                });

            Assert.That(
                second.ReappliedSiblingPaths,
                Is.EquivalentTo(new[] { ProjectRelativePath(PayloadFileName) }),
                FormatOutcomes(second));
            HotReloadMethodOutcome skipped = FindSkippedWire(second);
            Assert.That(skipped, Is.Not.Null, "Missing skipped row for Wire.\n" + FormatOutcomes(second));
            Assert.That(
                skipped.Reason,
                Does.Contain("Pass '" + ProjectRelativePath(RegistryFileName) + "'"),
                skipped.Reason);
        }

        private static HotReloadMethodOutcome FindSkippedWire(HotReloadOrchestratorResult result)
        {
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                if (outcome.Kind == HotReloadMethodOutcomeKind.Skipped
                    && outcome.Method != null
                    && outcome.Method.Contains(".Wire("))
                {
                    return outcome;
                }
            }

            return null;
        }

        private static string ReplaceOnce(string source, string anchor, string replacement)
        {
            Assert.That(source, Does.Contain(anchor), "Precondition: anchor must exist.");
            return source.Replace(anchor, replacement);
        }

        private static string FixturePath(string fileName)
        {
            string path = Path.GetFullPath(
                Path.Combine(Application.dataPath, "Tests", "Editor", "HotReload", fileName));
            Assert.That(File.Exists(path), Is.True, "Fixture missing: " + path);
            return path;
        }

        private static string ProjectRelativePath(string fileName)
        {
            return "Assets/Tests/Editor/HotReload/" + fileName;
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
