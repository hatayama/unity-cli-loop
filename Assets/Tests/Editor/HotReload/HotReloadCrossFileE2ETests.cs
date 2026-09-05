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
    /// End-to-end EditMode coverage for reloading several edited files of one compilation
    /// assembly in a single worker run and a single shim assembly.
    /// </summary>
    public class HotReloadCrossFileE2ETests
    {
        private const string HostFileName = "HotReloadCrossFileAddedMemberHost.cs";
        private const string CallerFileName = "HotReloadCrossFileAddedMemberCaller.cs";
        private const string CrossAssemblyFileName = "HotReloadCallSiteCrossAssemblyCaller.cs";
        // The registry keys added members by their display label, not by the worker wire key.
        private const string HostAddedMethodLabel =
            "io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload.HotReloadCrossFileAddedMemberHost.Added()";

        private const string HostValueAnchor = "        public int Value()";
        private const string HostScaledBodyAnchor = "return factor;";
        private const string CallerCallBodyAnchor = "return host.Value();";
        private const string CallerOtherBodyAnchor = "return 7;";
        private const string CallerGatedCallBodyAnchor = "return host.Gated(1);";
        private const string CrossAssemblyBodyAnchor = "return 8;";

        [SetUp]
        public void SetUp()
        {
            HotReloadPatcher.RevertAll();
            HotReloadAutoRefreshHold.Sync(HotReloadPatcher.ActiveChangeCount);
        }

        [TearDown]
        public void TearDown()
        {
            HotReloadPatcher.RevertAll();
            HotReloadAutoRefreshHold.Sync(HotReloadPatcher.ActiveChangeCount);
            VibeLogger.ClearMemoryLogs();
        }

        /// <summary>
        /// What: a body edited in the caller file binds to a method added in the host file within
        /// one reload, both files are applied, every outcome names the file that declares it, and
        /// the patched caller returns the value the added method produces.
        /// </summary>
        [Test]
        public async Task Run_CallerFileUsesMethodAddedInOtherFile_AppliesBothAndUpdatesRuntime()
        {
            HotReloadOrchestratorResult result = await RunPairAsync(
                "CrossFileAddedMethod",
                InsertHostMember("        public int Added()\n        {\n            return 41;\n        }\n\n"),
                ReplaceCallerBody(CallerCallBodyAnchor, "return host.Added() + 1;"));

            AssertNoFailure(result);
            AssertKind(result, HotReloadMethodOutcomeKind.Added, "Added");
            AssertKind(result, HotReloadMethodOutcomeKind.Patched, "Call");
            AssertOutcomeFilePathsMatchDeclaringFile(result);
            Assert.That(
                new HotReloadCrossFileAddedMemberCaller().Call(new HotReloadCrossFileAddedMemberHost()),
                Is.EqualTo(42));
            Assert.That(HotReloadShimRegistry.HasGeneration(HostProjectRelativePath()), Is.True);
            Assert.That(HotReloadShimRegistry.HasGeneration(CallerProjectRelativePath()), Is.True);
            Assert.That(
                HotReloadAddedMemberRegistry.IsActiveMember(HostProjectRelativePath(), HostAddedMethodLabel),
                Is.True);
            Assert.That(
                HotReloadAddedMemberRegistry.IsActiveMember(CallerProjectRelativePath(), HostAddedMethodLabel),
                Is.False);
        }

        /// <summary>
        /// What: a field added in the host file is readable and writable from the caller file's
        /// edited body within one reload, the field survives across calls, and the run reports
        /// that one added field once.
        /// </summary>
        [Test]
        public async Task Run_CallerFileUsesFieldAddedInOtherFile_AppliesBothAndUpdatesRuntime()
        {
            HotReloadOrchestratorResult result = await RunPairAsync(
                "CrossFileAddedField",
                InsertHostMember("        public int Counter;\n\n"),
                ReplaceCallerBody(
                    CallerCallBodyAnchor,
                    "host.Counter += 1;\n            return host.Counter + 40;"));

            AssertNoFailure(result);
            AssertKind(result, HotReloadMethodOutcomeKind.Patched, "Call");
            HotReloadCrossFileAddedMemberCaller caller = new HotReloadCrossFileAddedMemberCaller();
            HotReloadCrossFileAddedMemberHost host = new HotReloadCrossFileAddedMemberHost();
            Assert.That(caller.Call(host), Is.EqualTo(41));
            Assert.That(caller.Call(host), Is.EqualTo(42));
            Assert.That(
                HotReloadAddedFieldRegistry.GetFieldsForType(
                    typeof(HotReloadCrossFileAddedMemberHost).FullName),
                Is.EqualTo(new[] { "Counter" }));
            Assert.That(result.AddedFields, Has.Length.EqualTo(1));
            Assert.That(result.AddedFields[0], Does.Contain("Counter"));
            Assert.That(CountAddedFieldsLifetimeWarnings(result), Is.EqualTo(1));
        }

        /// <summary>
        /// What: calling a method that only an unedited sibling file would declare still fails,
        /// because a member the reload does not carry cannot be emitted into the shim.
        /// </summary>
        [Test]
        public async Task Run_CallerFileOnly_WithoutHostFile_FailsWithNewMemberHint()
        {
            string callerPath = FixturePath(CallerFileName);

            HotReloadOrchestratorResult result = await HotReloadOrchestrator.RunAsync(
                new[] { callerPath },
                HotReloadTestSourceWriter.WriteEditedSource(
                    "CrossFileCallerOnly.cs",
                    ReplaceCallerBody(CallerCallBodyAnchor, "return host.Added() + 1;")),
                CancellationToken.None);

            HotReloadMethodOutcome failure = FindOutcome(result, HotReloadMethodOutcomeKind.Failed, "Call");
            Assert.That(failure.Reason, Does.Contain(HotReloadConstants.NewMemberCompileHint));
        }

        /// <summary>
        /// What: a compile error in one file of the group takes down only that file — its other
        /// edited method reports the file-atomic skip and it starts no generation — while the
        /// healthy sibling file is applied and its runtime is updated.
        /// </summary>
        [Test]
        public async Task Run_TwoFilesOneHasCompileError_AppliesHealthyFileAndFailsOnlyBrokenFile()
        {
            string callerSource = ReplaceCallerBody(
                CallerCallBodyAnchor,
                "int broken = \"not an int\";\n            return broken;");
            callerSource = ReplaceInSource(callerSource, CallerOtherBodyAnchor, "return 8;");

            HotReloadOrchestratorResult result = await RunPairAsync(
                "CrossFileBrokenCaller",
                ReplaceHostBody(HostScaledBodyAnchor, "return factor + 100;"),
                callerSource);

            AssertKind(result, HotReloadMethodOutcomeKind.Patched, "Scaled");
            HotReloadMethodOutcome failure = FindOutcome(result, HotReloadMethodOutcomeKind.Failed, "Call");
            HotReloadMethodOutcome atomicSkip = FindOutcome(result, HotReloadMethodOutcomeKind.Skipped, "Other");
            Assert.That(atomicSkip.Reason, Is.EqualTo(HotReloadConstants.AtomicFileSkipReason));
            Assert.That(failure.FilePath, Is.EqualTo(FixturePath(CallerFileName)));
            Assert.That(atomicSkip.FilePath, Is.EqualTo(FixturePath(CallerFileName)));
            Assert.That(new HotReloadCrossFileAddedMemberHost().Scaled(1), Is.EqualTo(101));
            Assert.That(HotReloadShimRegistry.HasGeneration(HostProjectRelativePath()), Is.True);
            Assert.That(HotReloadShimRegistry.HasGeneration(CallerProjectRelativePath()), Is.False);
        }

        /// <summary>
        /// What: when the file that declares an added method fails to compile, the sibling file's
        /// body that calls it is skipped as an isolated added-method caller instead of being
        /// reported as one more file-atomic skip, and nothing of the reload is applied because
        /// the surviving file's only edited body was that caller.
        /// </summary>
        [Test]
        public async Task Run_TwoFilesBrokenFileDeclaresAddedMethodUsedByOther_SkipsCallerBodyAndAppliesNothing()
        {
            string hostSource = InsertHostMember(
                "        public int Added()\n        {\n            return 41;\n        }\n\n");
            hostSource = ReplaceInSource(
                hostSource,
                "return 1;",
                "int broken = \"not an int\";\n            return broken;");
            hostSource = ReplaceInSource(hostSource, HostScaledBodyAnchor, "return factor + 5;");

            HotReloadOrchestratorResult result = await RunPairAsync(
                "CrossFileBrokenHostAddedMethod",
                hostSource,
                ReplaceCallerBody(CallerCallBodyAnchor, "return host.Added() + 1;"));

            FindOutcome(result, HotReloadMethodOutcomeKind.Failed, "Value");
            HotReloadMethodOutcome hostSkip = FindOutcome(result, HotReloadMethodOutcomeKind.Skipped, "Scaled");
            Assert.That(hostSkip.Reason, Is.EqualTo(HotReloadConstants.AtomicFileSkipReason));
            HotReloadMethodOutcome callerSkip = FindOutcome(result, HotReloadMethodOutcomeKind.Skipped, "Call(");
            Assert.That(callerSkip.Reason, Is.EqualTo(HotReloadConstants.IsolatedAddedMethodCallerSkipReason));
            Assert.That(callerSkip.FilePath, Is.EqualTo(FixturePath(CallerFileName)));
            AssertNothingApplied(result);
        }

        /// <summary>
        /// What: two edited files of different compilation assemblies are processed as separate
        /// groups, so both are applied in one run and a compile error in one of them leaves the
        /// other applied.
        /// </summary>
        [Test]
        public async Task Run_TwoFilesDifferentAssemblies_ProcessedAsSeparateGroups()
        {
            string hostPath = FixturePath(HostFileName);
            string crossAssemblyPath = CrossAssemblyFixturePath();
            string crossAssemblyOnDisk = File.ReadAllText(crossAssemblyPath);

            HotReloadOrchestratorResult applied = await HotReloadOrchestrator.RunAsync(
                new[] { hostPath, crossAssemblyPath },
                contentPathOverride: null,
                CancellationToken.None,
                new Dictionary<string, string>
                {
                    [hostPath] = HotReloadTestSourceWriter.WriteEditedSource(
                        "CrossAssemblyGroupHost.cs",
                        ReplaceHostBody(HostScaledBodyAnchor, "return factor + 200;")),
                    [crossAssemblyPath] = HotReloadTestSourceWriter.WriteEditedSource(
                        "CrossAssemblyGroupCaller.cs",
                        ReplaceInSource(crossAssemblyOnDisk, CrossAssemblyBodyAnchor, "return 9;"))
                });

            AssertNoFailure(applied);
            AssertKind(applied, HotReloadMethodOutcomeKind.Patched, "Scaled");
            AssertKind(applied, HotReloadMethodOutcomeKind.Patched, "CalledFromCrossAssembly");

            HotReloadPatcher.RevertAll();
            HotReloadAutoRefreshHold.Sync(HotReloadPatcher.ActiveChangeCount);

            HotReloadOrchestratorResult isolated = await HotReloadOrchestrator.RunAsync(
                new[] { hostPath, crossAssemblyPath },
                contentPathOverride: null,
                CancellationToken.None,
                new Dictionary<string, string>
                {
                    [hostPath] = HotReloadTestSourceWriter.WriteEditedSource(
                        "CrossAssemblyGroupHostHealthy.cs",
                        ReplaceHostBody(HostScaledBodyAnchor, "return factor + 300;")),
                    [crossAssemblyPath] = HotReloadTestSourceWriter.WriteEditedSource(
                        "CrossAssemblyGroupCallerBroken.cs",
                        ReplaceInSource(
                            crossAssemblyOnDisk,
                            CrossAssemblyBodyAnchor,
                            "int broken = \"not an int\";\n            return broken;"))
                });

            AssertKind(isolated, HotReloadMethodOutcomeKind.Patched, "Scaled");
            HotReloadMethodOutcome failure =
                FindOutcome(isolated, HotReloadMethodOutcomeKind.Failed, "CalledFromCrossAssembly");
            Assert.That(failure.FilePath, Is.EqualTo(crossAssemblyPath));
            Assert.That(new HotReloadCrossFileAddedMemberHost().Scaled(1), Is.EqualTo(301));
        }

        /// <summary>
        /// What: changing a method's return type is applied when its only call site was edited in
        /// another file of the same reload, because the signature-change gate sees that caller
        /// covered by this run.
        /// </summary>
        [Test]
        public async Task Run_ReturnTypeChange_CallerInOtherEditedFile_CoversGate()
        {
            HotReloadOrchestratorResult result = await RunPairAsync(
                "CrossFileReturnTypeChange",
                ReplaceInSource(
                    ReadFixture(HostFileName),
                    "public int Gated(int seed)",
                    "public long Gated(int seed)"),
                ReplaceCallerBody(CallerGatedCallBodyAnchor, "return (int)host.Gated(2);"));

            AssertNoFailure(result);
            AssertKind(result, HotReloadMethodOutcomeKind.Patched, "CallGated");
            Assert.That(
                FindReplacementOutcome(result, ".Gated(").Kind,
                Is.Not.EqualTo(HotReloadMethodOutcomeKind.Skipped),
                "The gate must treat a caller edited in the same reload as covered.\n"
                + FormatOutcomes(result));
        }

        private static async Task<HotReloadOrchestratorResult> RunPairAsync(
            string editedFileNamePrefix,
            string editedHostSource,
            string editedCallerSource)
        {
            string hostPath = FixturePath(HostFileName);
            string callerPath = FixturePath(CallerFileName);
            return await HotReloadOrchestrator.RunAsync(
                new[] { hostPath, callerPath },
                contentPathOverride: null,
                CancellationToken.None,
                new Dictionary<string, string>
                {
                    [hostPath] = HotReloadTestSourceWriter.WriteEditedSource(
                        editedFileNamePrefix + "Host.cs",
                        editedHostSource),
                    [callerPath] = HotReloadTestSourceWriter.WriteEditedSource(
                        editedFileNamePrefix + "Caller.cs",
                        editedCallerSource)
                });
        }

        private static string InsertHostMember(string memberText)
        {
            string source = ReadFixture(HostFileName);
            Assert.That(source, Does.Contain(HostValueAnchor), "Precondition: host anchor must exist.");
            return source.Replace(HostValueAnchor, memberText + HostValueAnchor, StringComparison.Ordinal);
        }

        private static string ReplaceHostBody(string bodyAnchor, string bodyText)
        {
            return ReplaceInSource(ReadFixture(HostFileName), bodyAnchor, bodyText);
        }

        private static string ReplaceCallerBody(string bodyAnchor, string bodyText)
        {
            return ReplaceInSource(ReadFixture(CallerFileName), bodyAnchor, bodyText);
        }

        private static string ReplaceInSource(string source, string anchor, string replacement)
        {
            Assert.That(source, Does.Contain(anchor), "Precondition: anchor must exist: " + anchor);
            return source.Replace(anchor, replacement, StringComparison.Ordinal);
        }

        private static string ReadFixture(string fileName)
        {
            return File.ReadAllText(FixturePath(fileName));
        }

        private static string FixturePath(string fileName)
        {
            string path = Path.GetFullPath(
                Path.Combine(Application.dataPath, "Tests", "Editor", "HotReload", fileName));
            Assert.That(File.Exists(path), Is.True, "Fixture missing: " + path);
            return path;
        }

        private static string CrossAssemblyFixturePath()
        {
            string path = Path.GetFullPath(
                Path.Combine(
                    Application.dataPath,
                    "Tests",
                    "Editor",
                    "HotReloadCallSiteCrossAssembly",
                    CrossAssemblyFileName));
            Assert.That(File.Exists(path), Is.True, "Fixture missing: " + path);
            return path;
        }

        private static string HostProjectRelativePath()
        {
            return "Assets/Tests/Editor/HotReload/" + HostFileName;
        }

        private static string CallerProjectRelativePath()
        {
            return "Assets/Tests/Editor/HotReload/" + CallerFileName;
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

        private static void AssertNothingApplied(HotReloadOrchestratorResult result)
        {
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                Assert.That(
                    outcome.Kind,
                    Is.Not.EqualTo(HotReloadMethodOutcomeKind.Patched),
                    "Nothing may be applied here.\n" + FormatOutcomes(result));
                Assert.That(
                    outcome.Kind,
                    Is.Not.EqualTo(HotReloadMethodOutcomeKind.Added),
                    "Nothing may be applied here.\n" + FormatOutcomes(result));
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

        // The outcome of the method whose signature the edit replaced, whatever kind the gate
        // and the applier gave it.
        private static HotReloadMethodOutcome FindReplacementOutcome(
            HotReloadOrchestratorResult result,
            string methodNamePart)
        {
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                if (outcome.Method != null && outcome.Method.Contains(methodNamePart))
                {
                    return outcome;
                }
            }

            Assert.Fail("Expected an outcome for " + methodNamePart + ".\n" + FormatOutcomes(result));
            return null;
        }

        // Every outcome must report the fixture file whose type declares the method, so a group
        // run cannot attribute one file's rows to another.
        private static void AssertOutcomeFilePathsMatchDeclaringFile(HotReloadOrchestratorResult result)
        {
            string hostPath = FixturePath(HostFileName);
            string callerPath = FixturePath(CallerFileName);
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                string expected = outcome.Method != null
                    && outcome.Method.Contains(nameof(HotReloadCrossFileAddedMemberCaller))
                        ? callerPath
                        : hostPath;
                Assert.That(
                    outcome.FilePath,
                    Is.EqualTo(expected),
                    "Outcome names the wrong file: " + outcome.Method);
            }
        }

        private static int CountAddedFieldsLifetimeWarnings(HotReloadOrchestratorResult result)
        {
            string prefix = HotReloadConstants.AddedFieldsLifetimeWarningFormat.Substring(
                0,
                HotReloadConstants.AddedFieldsLifetimeWarningFormat.IndexOf("{0}", StringComparison.Ordinal));
            int count = 0;
            foreach (string warning in result.Warnings)
            {
                if (warning != null && warning.StartsWith(prefix, StringComparison.Ordinal))
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
                lines.Add(outcome.Kind + " " + outcome.Method + " @" + outcome.FilePath + " :: " + outcome.Reason);
            }

            return string.Join("\n", lines);
        }
    }
}
