using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// End-to-end coverage of the warning a run gives when a caller that did not apply keeps
    /// calling an added member a later reload retired: the member leaves --status, but the caller's
    /// earlier patch still runs the member's earlier body.
    /// </summary>
    public class HotReloadStaleAddedMemberCallE2ETests
    {
        private const string HostFileName = "HotReloadCrossFileAddedMemberHost.cs";
        private const string CallerFileName = "HotReloadCrossFileAddedMemberCaller.cs";
        private const string HostValueAnchor = "        public int Value()";
        private const string CallerCallBodyAnchor = "return host.Value();";
        private const string CallerOtherBodyAnchor = "            return 7;";
        private const string CallerSecondMemberAnchor = "        // Second editable member of this file";
        private const string OneArgumentAddedMember =
            "        public int Added(int seed)\n        {\n            return seed + 40;\n        }\n\n";
        private const string TwoArgumentAddedMember =
            "        public int Added(int seed, int extra)\n        {\n            return seed + extra;\n        }\n\n";

        // A type error in an existing body fails the caller's shim compile, so isolation fails the
        // whole file and leaves its earlier generation in place, whatever the rest of the file says.
        private const string BrokenOtherBody = "            int broken = \"not an int\";\n            return broken;";

        private HotReloadDomainTestScope _scope;

        [SetUp]
        public void SetUp()
        {
            _scope = new HotReloadDomainTestScope();
        }

        [TearDown]
        public void TearDown()
        {
            _scope.Dispose();
        }

        /// <summary>
        /// What: a caller that fails in the run that changes the signature of the added member it
        /// calls keeps running the member's earlier body, and that run names the call; a later run
        /// given only the member's unchanged file names it again; the run that applies the caller on
        /// the new signature no longer does.
        /// </summary>
        [Test]
        public async Task Run_CallerFailsWhenItsAddedCalleeChangesSignature_NamesTheCallUntilTheCallerApplies()
        {
            string hostPath = FixturePath(HostFileName);
            string callerPath = FixturePath(CallerFileName);
            HotReloadOrchestratorResult first = await RunAsync(
                new[] { hostPath, callerPath },
                Override(hostPath, "StaleCallFailedHost1.cs", InsertHostMember(OneArgumentAddedMember)),
                Override(callerPath, "StaleCallFailedCaller1.cs", ReplaceInCaller(CallerCallBodyAnchor, "return host.Added(1);")));
            AssertNoFailure(first);
            HotReloadStaleAddedMemberCallsWarnings.AssertNone(first.Warnings);
            Assert.That(CallThroughCaller(), Is.EqualTo(41));

            string secondHostEditPath = HotReloadTestSourceWriter.WriteEditedSource(
                "StaleCallFailedHost2.cs",
                InsertHostMember(TwoArgumentAddedMember));
            HotReloadOrchestratorResult second = await RunAsync(
                new[] { hostPath, callerPath },
                (hostPath, secondHostEditPath),
                Override(
                    callerPath,
                    "StaleCallFailedCaller2.cs",
                    ReplaceInCaller(CallerCallBodyAnchor, "return host.Added(1);", CallerOtherBodyAnchor, BrokenOtherBody)));
            AssertHasOutcomeForFile(second, callerPath, HotReloadMethodOutcomeKind.Failed);
            AssertHasOutcome(second, HotReloadMethodOutcomeKind.Added, ".Added(System.Int32,System.Int32)");
            Assert.That(second.Warnings, Does.Contain(ExpectedCallIntoOneArgumentAdded()), string.Join("\n", second.Warnings));
            Assert.That(CallThroughCaller(), Is.EqualTo(41));

            HotReloadOrchestratorResult third = await RunAsync(new[] { hostPath }, (hostPath, secondHostEditPath));
            AssertHasOutcome(third, HotReloadMethodOutcomeKind.AlreadyActive, ".Added(");
            Assert.That(third.Warnings, Does.Contain(ExpectedCallIntoOneArgumentAdded()), string.Join("\n", third.Warnings));

            HotReloadOrchestratorResult fourth = await RunAsync(
                new[] { hostPath, callerPath },
                (hostPath, secondHostEditPath),
                Override(callerPath, "StaleCallFailedCaller4.cs", ReplaceInCaller(CallerCallBodyAnchor, "return host.Added(1, 2);")));
            AssertNoFailure(fourth);
            HotReloadStaleAddedMemberCallsWarnings.AssertNone(fourth.Warnings);
            Assert.That(CallThroughCaller(), Is.EqualTo(3));
        }

        /// <summary>
        /// What: a caller pulled back in to re-bind when the added member it calls changes signature
        /// is Skipped because its unchanged body no longer binds, keeps running the member's earlier
        /// body, and the run names the call beside the Skipped-only sibling warning.
        /// </summary>
        [Test]
        public async Task Run_SiblingSkippedWhenItsAddedCalleeChangesSignature_NamesTheCall()
        {
            string hostPath = FixturePath(HostFileName);
            string callerPath = FixturePath(CallerFileName);
            string firstCallerEditPath = HotReloadTestSourceWriter.WriteEditedSource(
                "StaleCallSiblingCaller1.cs",
                ReplaceInCaller(CallerCallBodyAnchor, "return host.Added(1);"));
            HotReloadOrchestratorResult first = await RunAsync(
                new[] { hostPath, callerPath },
                Override(hostPath, "StaleCallSiblingHost1.cs", InsertHostMember(OneArgumentAddedMember)),
                (callerPath, firstCallerEditPath));
            AssertNoFailure(first);

            HotReloadOrchestratorResult second = await RunAsync(
                new[] { hostPath },
                Override(hostPath, "StaleCallSiblingHost2.cs", InsertHostMember(TwoArgumentAddedMember)),
                (callerPath, firstCallerEditPath));

            AssertHasOutcomeForFile(second, CallerProjectRelativePath(), HotReloadMethodOutcomeKind.Skipped);
            Assert.That(
                second.Warnings,
                Does.Contain(
                    string.Format(
                        HotReloadConstants.ActiveSiblingRebindSkippedOnlyWarningFormat,
                        CallerProjectRelativePath())),
                string.Join("\n", second.Warnings));
            Assert.That(second.Warnings, Does.Contain(ExpectedCallIntoOneArgumentAdded()), string.Join("\n", second.Warnings));
            Assert.That(CallThroughCaller(), Is.EqualTo(41));
        }

        /// <summary>
        /// What: an added member that stays registered because its own file failed, and whose body
        /// calls an added member another file retired, is the caller the run names; the patch that
        /// calls the still-registered added member is not named.
        /// </summary>
        [Test]
        public async Task Run_RegisteredAddedMemberCallsARetiredAddedMember_NamesTheAddedMember()
        {
            string hostPath = FixturePath(HostFileName);
            string callerPath = FixturePath(CallerFileName);
            const string helperMember =
                "        public int Helper(HotReloadCrossFileAddedMemberHost host)\n        {\n            return host.Added(1);\n        }\n\n";
            HotReloadOrchestratorResult first = await RunAsync(
                new[] { hostPath, callerPath },
                Override(hostPath, "StaleCallAddedCallerHost1.cs", InsertHostMember(OneArgumentAddedMember)),
                Override(
                    callerPath,
                    "StaleCallAddedCallerCaller1.cs",
                    ReplaceInCaller(
                        CallerCallBodyAnchor,
                        "return Helper(host);",
                        CallerSecondMemberAnchor,
                        helperMember + CallerSecondMemberAnchor)));
            AssertNoFailure(first);
            Assert.That(CallThroughCaller(), Is.EqualTo(41));

            HotReloadOrchestratorResult second = await RunAsync(
                new[] { hostPath, callerPath },
                Override(hostPath, "StaleCallAddedCallerHost2.cs", InsertHostMember(TwoArgumentAddedMember)),
                Override(
                    callerPath,
                    "StaleCallAddedCallerCaller2.cs",
                    ReplaceInCaller(
                        CallerCallBodyAnchor,
                        "return Helper(host);",
                        CallerSecondMemberAnchor,
                        helperMember.Replace("host.Added(1)", "host.Added(1, 0)") + CallerSecondMemberAnchor,
                        CallerOtherBodyAnchor,
                        BrokenOtherBody)));

            AssertHasOutcomeForFile(second, callerPath, HotReloadMethodOutcomeKind.Failed);
            string helperLabel = HotReloadMethodKeys.FormatMethodLabelParts(
                new HotReloadMetadataTypeName(typeof(HotReloadCrossFileAddedMemberCaller).FullName),
                "Helper",
                new[] { typeof(HotReloadCrossFileAddedMemberHost).FullName },
                0);
            Assert.That(
                second.Warnings,
                Does.Contain(
                    HotReloadStaleAddedMemberCallsWarnings.Expected(
                        HotReloadStaleAddedMemberCallsWarnings.Pair(helperLabel, OneArgumentAddedLabel()))),
                string.Join("\n", second.Warnings));
            Assert.That(CallThroughCaller(), Is.EqualTo(41));
        }

        private static string ExpectedCallIntoOneArgumentAdded()
        {
            string callLabel = HotReloadMethodKeys.FormatMethodLabel(
                typeof(HotReloadCrossFileAddedMemberCaller).GetMethod(nameof(HotReloadCrossFileAddedMemberCaller.Call)));
            return HotReloadStaleAddedMemberCallsWarnings.Expected(
                HotReloadStaleAddedMemberCallsWarnings.Pair(callLabel, OneArgumentAddedLabel()));
        }

        private static string OneArgumentAddedLabel()
        {
            return HotReloadMethodKeys.FormatMethodLabelParts(
                new HotReloadMetadataTypeName(typeof(HotReloadCrossFileAddedMemberHost).FullName),
                "Added",
                new[] { "System.Int32" },
                0);
        }

        private static int CallThroughCaller()
        {
            return new HotReloadCrossFileAddedMemberCaller().Call(new HotReloadCrossFileAddedMemberHost());
        }

        private static (string Path, string EditedSourcePath) Override(string path, string editedFileName, string source)
        {
            return (path, HotReloadTestSourceWriter.WriteEditedSource(editedFileName, source));
        }

        private static Task<HotReloadOrchestratorResult> RunAsync(
            string[] files,
            params (string Path, string EditedSourcePath)[] overrides)
        {
            Dictionary<string, string> overrideByFile = new Dictionary<string, string>();
            foreach ((string path, string editedSourcePath) in overrides)
            {
                overrideByFile[path] = editedSourcePath;
            }

            return HotReloadCompositionRoot.Services.Orchestrator.RunAsync(
                files,
                contentPathOverride: null,
                CancellationToken.None,
                overrideByFile);
        }

        private static string InsertHostMember(string memberText)
        {
            string source = File.ReadAllText(FixturePath(HostFileName));
            Assert.That(source, Does.Contain(HostValueAnchor), "Precondition: host anchor must exist.");
            return source.Replace(HostValueAnchor, memberText + HostValueAnchor, StringComparison.Ordinal);
        }

        // Pairs of anchor and replacement, applied in order to the caller fixture.
        private static string ReplaceInCaller(params string[] anchorsAndReplacements)
        {
            string source = File.ReadAllText(FixturePath(CallerFileName));
            for (int index = 0; index < anchorsAndReplacements.Length; index += 2)
            {
                Assert.That(
                    source,
                    Does.Contain(anchorsAndReplacements[index]),
                    "Precondition: caller anchor must exist: " + anchorsAndReplacements[index]);
                source = source.Replace(
                    anchorsAndReplacements[index],
                    anchorsAndReplacements[index + 1],
                    StringComparison.Ordinal);
            }

            return source;
        }

        private static void AssertNoFailure(HotReloadOrchestratorResult result)
        {
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                Assert.That(
                    outcome.Kind,
                    Is.Not.EqualTo(HotReloadMethodOutcomeKind.Failed),
                    outcome.Method + " " + outcome.Reason);
            }
        }

        private static void AssertHasOutcome(
            HotReloadOrchestratorResult result,
            HotReloadMethodOutcomeKind kind,
            string methodToken)
        {
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                if (outcome.Kind == kind && outcome.Method.Contains(methodToken))
                {
                    return;
                }
            }

            Assert.Fail("No " + kind + " outcome containing '" + methodToken + "' in:\n" + FormatOutcomes(result));
        }

        private static void AssertHasOutcomeForFile(
            HotReloadOrchestratorResult result,
            string filePath,
            HotReloadMethodOutcomeKind kind)
        {
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                if (outcome.Kind == kind && outcome.FilePath == filePath)
                {
                    return;
                }
            }

            Assert.Fail("No " + kind + " outcome for '" + filePath + "' in:\n" + FormatOutcomes(result));
        }

        private static string FormatOutcomes(HotReloadOrchestratorResult result)
        {
            List<string> lines = new List<string>();
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                lines.Add(outcome.Kind + " " + outcome.Method + " [" + outcome.FilePath + "] " + outcome.Reason);
            }

            return string.Join("\n", lines);
        }

        private static string FixturePath(string fileName)
        {
            string path = Path.GetFullPath(
                Path.Combine(Application.dataPath, "Tests", "Editor", "HotReload", fileName));
            Assert.That(File.Exists(path), Is.True, "Fixture missing: " + path);
            return path;
        }

        private static string CallerProjectRelativePath()
        {
            return "Assets/Tests/Editor/HotReload/" + CallerFileName;
        }
    }
}
