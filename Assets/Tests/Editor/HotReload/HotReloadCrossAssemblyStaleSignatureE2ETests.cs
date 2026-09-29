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
    /// End-to-end EditMode coverage for the stale-signature warning and the signature-change gate
    /// when the only compiled callers of the edited host live in another assembly.
    /// </summary>
    public class HotReloadCrossAssemblyStaleSignatureE2ETests
    {
        private const string HostFileName = "HotReloadCrossAssemblyStaleSignatureHost.cs";
        private const string CallerFileName = "HotReloadCrossAssemblyStaleSignatureCaller.cs";
        private const string FixtureNamespace = "io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload.";
        private const string RemovedSignatureKey =
            FixtureNamespace + "HotReloadCrossAssemblyStaleSignatureHost::ToDelete(System.Int32)";
        private const string CallerKey =
            FixtureNamespace + "HotReloadCrossAssemblyStaleSignatureCaller::CallDeleted(System.Int32)";
        private const string CallerLabel =
            FixtureNamespace + "HotReloadCrossAssemblyStaleSignatureCaller.CallDeleted(System.Int32)";
        private const string ReturnTypeTargetLabel =
            FixtureNamespace + "HotReloadCrossAssemblyStaleSignatureHost.ReturnTypeTarget(System.Int32)";

        private const string HostUnrelatedAndToDeleteAnchor =
            "        public int Unrelated(int value)\n        {\n            return value;\n        }\n\n"
            + "        [MethodImpl(MethodImplOptions.NoInlining)]\n"
            + "        public int ToDelete(int value)\n        {\n            return value;\n        }";
        private const string HostUnrelatedEditedWithoutToDelete =
            "        public int Unrelated(int value)\n        {\n            return value + 1;\n        }";
        private const string HostUnrelatedAnchor =
            "        public int Unrelated(int value)\n        {\n            return value;\n        }";
        private const string HostUnrelatedEdited =
            "        public int Unrelated(int value)\n        {\n            return value + 1;\n        }";
        private const string HostReturnTypeTargetAnchor =
            "        public int ReturnTypeTarget(int value)\n        {\n            return value;\n        }";
        private const string HostReturnTypeTargetWidened =
            "        public long ReturnTypeTarget(int value)\n        {\n            return value + 1L;\n        }";
        private const string CallerDeletedCallAnchor =
            "            return new HotReloadCrossAssemblyStaleSignatureHost().ToDelete(value);";
        private const string CallerReturnTypeTargetCallAnchor =
            "            return new HotReloadCrossAssemblyStaleSignatureHost().ReturnTypeTarget(value);";

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
        /// What: when the caller's group runs before the host's group in one reload, the caller it
        /// patched is left out of the host's stale-signature warning, and the warning is gone
        /// because no compiled caller remains.
        /// </summary>
        [Test]
        public async Task Run_CrossAssemblyCallerPatchedInEarlierGroup_IsOmittedFromStaleSignatureWarning()
        {
            string hostPath = HostPath();
            string callerPath = CallerPath();

            HotReloadOrchestratorResult result = await RunAsync(
                new[] { callerPath, hostPath },
                new Dictionary<string, string>
                {
                    [callerPath] = WriteCallerWithoutDeletedCall("CrossAssemblyStaleCallerFirst.cs"),
                    [hostPath] = WriteHostWithoutToDelete("CrossAssemblyStaleHostSecond.cs")
                });

            AssertNoFailure(result);
            AssertKind(result, HotReloadMethodOutcomeKind.Patched, "CallDeleted");
            AssertKind(result, HotReloadMethodOutcomeKind.Patched, "Unrelated");
            Assert.That(FindStaleSignatureWarnings(result), Is.Empty, FormatWarnings(result));
        }

        /// <summary>
        /// What: when the host's group runs before the caller's group in one reload, the caller the
        /// later group patched is still left out of the host's stale-signature warning.
        /// </summary>
        [Test]
        public async Task Run_CrossAssemblyCallerPatchedInLaterGroup_IsOmittedFromStaleSignatureWarning()
        {
            string hostPath = HostPath();
            string callerPath = CallerPath();

            HotReloadOrchestratorResult result = await RunAsync(
                new[] { hostPath, callerPath },
                new Dictionary<string, string>
                {
                    [hostPath] = WriteHostWithoutToDelete("CrossAssemblyStaleHostFirst.cs"),
                    [callerPath] = WriteCallerWithoutDeletedCall("CrossAssemblyStaleCallerSecond.cs")
                });

            AssertNoFailure(result);
            AssertKind(result, HotReloadMethodOutcomeKind.Patched, "CallDeleted");
            AssertKind(result, HotReloadMethodOutcomeKind.Patched, "Unrelated");
            Assert.That(FindStaleSignatureWarnings(result), Is.Empty, FormatWarnings(result));
        }

        /// <summary>
        /// What: a reload of the host alone leaves out a caller that an earlier reload patched and
        /// that is still active.
        /// </summary>
        [Test]
        public async Task Run_HostOnlyReloadWithActiveCrossAssemblyCaller_IsOmittedFromStaleSignatureWarning()
        {
            string hostPath = HostPath();
            string callerPath = CallerPath();
            HotReloadOrchestratorResult callerRun = await RunAsync(
                new[] { callerPath },
                new Dictionary<string, string>
                {
                    [callerPath] = WriteCallerWithoutDeletedCall("CrossAssemblyStaleCallerEarlier.cs")
                });
            AssertKind(callerRun, HotReloadMethodOutcomeKind.Patched, "CallDeleted");

            HotReloadOrchestratorResult hostRun = await RunAsync(
                new[] { hostPath },
                new Dictionary<string, string>
                {
                    [hostPath] = WriteHostWithoutToDelete("CrossAssemblyStaleHostAlone.cs")
                });

            AssertNoFailure(hostRun);
            AssertKind(hostRun, HotReloadMethodOutcomeKind.Patched, "Unrelated");
            Assert.That(FindStaleSignatureWarnings(hostRun), Is.Empty, FormatWarnings(hostRun));
        }

        /// <summary>
        /// What: a reload of the host alone still names a caller in another assembly that no reload
        /// patched, since it keeps running its compiled body.
        /// </summary>
        [Test]
        public async Task Run_HostOnlyReloadWithUnpatchedCrossAssemblyCaller_StillWarns()
        {
            string hostPath = HostPath();

            HotReloadOrchestratorResult result = await RunAsync(
                new[] { hostPath },
                new Dictionary<string, string>
                {
                    [hostPath] = WriteHostWithoutToDelete("CrossAssemblyStaleHostUnpatchedCaller.cs")
                });

            AssertNoFailure(result);
            Assert.That(
                FindStaleSignatureWarnings(result),
                Is.EqualTo(new[] { FormatStaleSignatureWarning() }),
                FormatWarnings(result));
        }

        /// <summary>
        /// What: a caller whose earlier patch is active when the host's group runs, but that a later
        /// group of the same reload peels back to its compiled body, is still named, because the
        /// warning reflects the patches that are active when the reload ends.
        /// </summary>
        [Test]
        public async Task Run_CrossAssemblyCallerPeeledLaterInSameRun_StillWarns()
        {
            string hostPath = HostPath();
            string callerPath = CallerPath();
            HotReloadOrchestratorResult callerRun = await RunAsync(
                new[] { callerPath },
                new Dictionary<string, string>
                {
                    [callerPath] = WriteCallerWithoutDeletedCall("CrossAssemblyStaleCallerToPeel.cs")
                });
            AssertKind(callerRun, HotReloadMethodOutcomeKind.Patched, "CallDeleted");

            // Why the caller keeps its on-disk content: it matches the compiled assembly, so its
            // group peels the earlier patch after the host's group has already been gated.
            HotReloadOrchestratorResult result = await RunAsync(
                new[] { hostPath, callerPath },
                new Dictionary<string, string>
                {
                    [hostPath] = WriteHostWithoutToDelete("CrossAssemblyStaleHostBeforePeel.cs")
                });

            AssertNoFailure(result);
            Assert.That(IsActive(CallerLabel), Is.False, "Precondition: the caller patch was peeled.");
            Assert.That(
                FindStaleSignatureWarnings(result),
                Is.EqualTo(new[] { FormatStaleSignatureWarning() }),
                FormatWarnings(result));
        }

        /// <summary>
        /// What: an active caller patch is left out even when its own body still calls the removed
        /// method, because the warning does not look inside patched bodies (documented limit).
        /// </summary>
        [Test]
        public async Task Run_ActiveCrossAssemblyCallerStillCallingRemovedMethod_IsOmitted()
        {
            string hostPath = HostPath();
            string callerPath = CallerPath();
            HotReloadOrchestratorResult callerRun = await RunAsync(
                new[] { callerPath },
                new Dictionary<string, string>
                {
                    [callerPath] = HotReloadTestSourceWriter.WriteEditedSource(
                        "CrossAssemblyStaleCallerStillCalling.cs",
                        ReplaceInSource(
                            File.ReadAllText(callerPath),
                            CallerDeletedCallAnchor,
                            "            return new HotReloadCrossAssemblyStaleSignatureHost().ToDelete(value) + 7;"))
                });
            AssertKind(callerRun, HotReloadMethodOutcomeKind.Patched, "CallDeleted");

            HotReloadOrchestratorResult hostRun = await RunAsync(
                new[] { hostPath },
                new Dictionary<string, string>
                {
                    [hostPath] = WriteHostWithoutToDelete("CrossAssemblyStaleHostAfterStillCalling.cs")
                });

            AssertNoFailure(hostRun);
            Assert.That(FindStaleSignatureWarnings(hostRun), Is.Empty, FormatWarnings(hostRun));
        }

        /// <summary>
        /// What: a return-type change is still refused when its only compiled caller lives in
        /// another assembly and an earlier reload patched it, because that patch was compiled
        /// against the old signature.
        /// </summary>
        [Test]
        public async Task Run_ReturnTypeChangeWithOnlyCrossAssemblyActiveCaller_StaysGated()
        {
            string hostPath = HostPath();
            string callerPath = CallerPath();
            HotReloadOrchestratorResult callerRun = await RunAsync(
                new[] { callerPath },
                new Dictionary<string, string>
                {
                    [callerPath] = HotReloadTestSourceWriter.WriteEditedSource(
                        "CrossAssemblyGatedCaller.cs",
                        ReplaceInSource(
                            File.ReadAllText(callerPath),
                            CallerReturnTypeTargetCallAnchor,
                            "            return new HotReloadCrossAssemblyStaleSignatureHost().ReturnTypeTarget(value) + 7;"))
                });
            AssertKind(callerRun, HotReloadMethodOutcomeKind.Patched, "CallReturnTypeTarget");
            string hostSource = ReplaceInSource(
                ReplaceInSource(File.ReadAllText(hostPath), HostReturnTypeTargetAnchor, HostReturnTypeTargetWidened),
                HostUnrelatedAnchor,
                HostUnrelatedEdited);

            HotReloadOrchestratorResult hostRun = await RunAsync(
                new[] { hostPath },
                new Dictionary<string, string>
                {
                    [hostPath] = HotReloadTestSourceWriter.WriteEditedSource("CrossAssemblyGatedHost.cs", hostSource)
                });

            HotReloadMethodOutcome skipped =
                FindOutcome(hostRun, HotReloadMethodOutcomeKind.Skipped, "Host.ReturnTypeTarget");
            Assert.That(
                skipped.Reason,
                Is.EqualTo(string.Format(HotReloadConstants.SignatureChangedGateSkipReasonFormat, ReturnTypeTargetLabel)));
        }

        private static async Task<HotReloadOrchestratorResult> RunAsync(
            string[] files,
            Dictionary<string, string> contentPathOverrideByFile)
        {
            return await HotReloadCompositionRoot.Services.Orchestrator.RunAsync(
                files,
                contentPathOverride: null,
                CancellationToken.None,
                contentPathOverrideByFile);
        }

        private static string WriteHostWithoutToDelete(string fileName)
        {
            return HotReloadTestSourceWriter.WriteEditedSource(
                fileName,
                ReplaceInSource(
                    File.ReadAllText(HostPath()),
                    HostUnrelatedAndToDeleteAnchor,
                    HostUnrelatedEditedWithoutToDelete));
        }

        private static string WriteCallerWithoutDeletedCall(string fileName)
        {
            return HotReloadTestSourceWriter.WriteEditedSource(
                fileName,
                ReplaceInSource(
                    File.ReadAllText(CallerPath()),
                    CallerDeletedCallAnchor,
                    "            return value + 7;"));
        }

        private static List<string> FindStaleSignatureWarnings(HotReloadOrchestratorResult result)
        {
            // Why the quoted key: only the stale-signature warning quotes the removed wire key.
            string quotedKey = "'" + RemovedSignatureKey + "'";
            List<string> warnings = new List<string>();
            foreach (string warning in result.Warnings)
            {
                if (warning.Contains(quotedKey))
                {
                    warnings.Add(warning);
                }
            }

            return warnings;
        }

        private static string FormatStaleSignatureWarning()
        {
            return string.Format(
                HotReloadConstants.StaleSignatureCallersWarningFormat,
                RemovedSignatureKey,
                CallerKey);
        }

        private static bool IsActive(string methodLabel)
        {
            foreach (HotReloadActivePatchInfo patch in HotReloadCompositionRoot.Services.Patcher.DescribeActivePatches())
            {
                if (patch.MethodKey == methodLabel)
                {
                    return true;
                }
            }

            return false;
        }

        private static string ReplaceInSource(string source, string anchor, string replacement)
        {
            Assert.That(source, Does.Contain(anchor), "Precondition: anchor must exist: " + anchor);
            return source.Replace(anchor, replacement, StringComparison.Ordinal);
        }

        private static string HostPath()
        {
            return FixturePath(Path.Combine("HotReload", HostFileName));
        }

        private static string CallerPath()
        {
            return FixturePath(Path.Combine("HotReloadCallSiteCrossAssembly", CallerFileName));
        }

        private static string FixturePath(string relativePath)
        {
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "Tests", "Editor", relativePath));
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

        private static void AssertKind(
            HotReloadOrchestratorResult result,
            HotReloadMethodOutcomeKind kind,
            string methodNamePart)
        {
            FindOutcome(result, kind, methodNamePart);
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

        private static string FormatWarnings(HotReloadOrchestratorResult result)
        {
            return "Warnings:\n" + string.Join("\n", result.Warnings) + "\nMethods:\n" + FormatOutcomes(result);
        }
    }
}
