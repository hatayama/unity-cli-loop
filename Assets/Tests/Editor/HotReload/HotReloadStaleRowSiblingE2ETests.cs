using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// End-to-end coverage of a file whose earlier patched method was deleted (a Stale row) while
    /// a member it added stays: a later reload of another file that calls the added member must
    /// still bring that file back, because the member exists only in its edited source.
    /// </summary>
    public class HotReloadStaleRowSiblingE2ETests : HotReloadIntroducedTypeE2ETestBase
    {
        private const string PayloadFileName = "HotReloadBindingSplitPayload.cs";
        private const string RegistryFileName = "HotReloadBindingSplitRegistry.cs";
        private const string ScaledBody = "return Value * 2;";
        private const string PayloadAnchor = "        public int Value;";
        private const string ScaledDeclaration =
            "        public int Scaled()\n"
            + "        {\n"
            + "            return Value * 3;\n"
            + "        }\n";
        private const string AddedMember =
            "\n"
            + "        [System.Runtime.CompilerServices.MethodImpl(\n"
            + "            System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]\n"
            + "        public int AddedOffset()\n"
            + "        {\n"
            + "            return Value + 5;\n"
            + "        }\n";
        private const string RaiseInvoke = "_handler?.Invoke(new HotReloadBindingSplitPayload { Value = value });";
        private const string RaiseThroughAddedMember =
            "_handler?.Invoke(new HotReloadBindingSplitPayload { Value = new HotReloadBindingSplitPayload { Value = value }.AddedOffset() });";

        // The owner of the introduced type is never written to disk, so the test assembly holds
        // an introduced type without a compile ever listing its file.
        private const string OwnerRequestedPath =
            "Assets/Tests/Editor/HotReload/UncompiledStaleRowIntroducedOwner.cs";
        private const string CallerFileName = "HotReloadCrossFileAddedMemberCaller.cs";
        private const string CallerBodyAnchor = "return host.Value();";
        private const string IntroducedTypeSimpleName = "HotReloadStaleRowIntroducedValue";

        /// <summary>
        /// What: in an assembly with no introduced type, a reload of only the file that calls the
        /// member a Stale-row file added brings that file back, so the caller is Patched with
        /// nothing Failed or Skipped, no changed-source warning, and the added member answers.
        /// Passing the Stale-row file again afterwards short-circuits to AlreadyActive.
        /// </summary>
        [Test]
        public async Task Run_ReloadOfTheCallerOnly_ReappliesTheStaleRowFile()
        {
            Dictionary<string, string> overrides = await ReloadTheCallerAfterTheStaleRowAsync(new Dictionary<string, string>());

            HotReloadOrchestratorResult again = await RunAsync(new[] { FixturePath(PayloadFileName) }, overrides);

            Assert.That(CountFailures(again), Is.Zero, DescribeOutcomes(again));
            Assert.That(CountKind(again, HotReloadMethodOutcomeKind.AlreadyActive), Is.Positive, DescribeOutcomes(again));
            Assert.That(CountKind(again, HotReloadMethodOutcomeKind.Skipped), Is.Zero, DescribeOutcomes(again));
        }

        /// <summary>
        /// What: in an assembly that owns an introduced type, a reload of only the file that calls
        /// the member a Stale-row file added brings that file back the same way, with no warning
        /// that the Stale-row file's source changed since it was applied.
        /// </summary>
        [Test]
        public async Task Run_ReloadOfTheCallerOnlyWithAnIntroducedType_ReappliesTheStaleRowFile()
        {
            await RunInIntroducedTypeDomainAsync(async readArtifact =>
            {
                string callerPath = FixturePath(CallerFileName);
                Dictionary<string, string> introducing = new Dictionary<string, string>
                {
                    [OwnerRequestedPath] = HotReloadTestSourceWriter.WriteEditedSource(
                        "StaleRowIntroducedOwner.cs",
                        BuildOwnerSource()),
                    [callerPath] = HotReloadTestSourceWriter.WriteEditedSource(
                        "StaleRowIntroducedCaller.cs",
                        ReplaceOnce(
                            File.ReadAllText(callerPath),
                            CallerBodyAnchor,
                            "return new " + IntroducedTypeSimpleName + "().Ping() + host.Value();"))
                };
                HotReloadOrchestratorResult introduced = await RunReloadAsync(OwnerRequestedPath, callerPath, introducing);
                Assert.That(CountFailures(introduced), Is.Zero, "Precondition: the type must be introduced.\n" + DescribeOutcomes(introduced));
                Assert.That(introduced.IntroducedTypes, Is.Not.Empty, "Precondition: the type must be introduced.\n" + DescribeOutcomes(introduced));

                await ReloadTheCallerAfterTheStaleRowAsync(introducing);
            });
        }

        // Runs the three reloads both tests share and returns the overrides the last one used,
        // so a later run reads the same bytes for every file.
        private static async Task<Dictionary<string, string>> ReloadTheCallerAfterTheStaleRowAsync(
            Dictionary<string, string> overrides)
        {
            string payloadPath = FixturePath(PayloadFileName);
            string registryPath = FixturePath(RegistryFileName);
            string payloadSource = File.ReadAllText(payloadPath);

            overrides[payloadPath] = HotReloadTestSourceWriter.WriteEditedSource(
                "StaleRowSiblingPayloadPatched.cs",
                ReplaceOnce(
                    ReplaceOnce(payloadSource, ScaledBody, "return Value * 3;"),
                    PayloadAnchor,
                    PayloadAnchor + "\n" + AddedMember));
            HotReloadOrchestratorResult patched = await RunAsync(new[] { payloadPath }, overrides);
            Assert.That(CountKindIn(patched, HotReloadMethodOutcomeKind.Patched, PayloadFileName), Is.EqualTo(1), "Precondition: Scaled must be Patched.\n" + DescribeOutcomes(patched));
            Assert.That(CountKindIn(patched, HotReloadMethodOutcomeKind.Added, PayloadFileName), Is.EqualTo(1), "Precondition: AddedOffset must be Added.\n" + DescribeOutcomes(patched));

            string patchedSource = File.ReadAllText(overrides[payloadPath]);
            overrides[payloadPath] = HotReloadTestSourceWriter.WriteEditedSource(
                "StaleRowSiblingPayloadScaledRemoved.cs",
                RemoveScaled(patchedSource));
            HotReloadOrchestratorResult stale = await RunAsync(new[] { payloadPath }, overrides);
            Assert.That(CountKindIn(stale, HotReloadMethodOutcomeKind.Stale, PayloadFileName), Is.EqualTo(1), "Precondition: Scaled must be Stale.\n" + DescribeOutcomes(stale));
            Assert.That(CountFailures(stale), Is.Zero, "Precondition: removing Scaled must fail nothing.\n" + DescribeOutcomes(stale));
            Assert.That(CountKind(stale, HotReloadMethodOutcomeKind.Skipped), Is.Zero, "Precondition: removing Scaled must skip nothing.\n" + DescribeOutcomes(stale));

            overrides[registryPath] = HotReloadTestSourceWriter.WriteEditedSource(
                "StaleRowSiblingRegistry.cs",
                ReplaceOnce(File.ReadAllText(registryPath), RaiseInvoke, RaiseThroughAddedMember));
            HotReloadOrchestratorResult caller = await RunAsync(new[] { registryPath }, overrides);

            Assert.That(CountFailures(caller), Is.Zero, Describe(caller));
            Assert.That(CountKind(caller, HotReloadMethodOutcomeKind.Skipped), Is.Zero, Describe(caller));
            Assert.That(CountKindIn(caller, HotReloadMethodOutcomeKind.Patched, RegistryFileName), Is.EqualTo(1), Describe(caller));
            Assert.That(caller.ReappliedSiblingPaths, Does.Contain(ProjectRelativePath(PayloadFileName)), Describe(caller));
            Assert.That(caller.Warnings, Has.None.Contains("source changed since they were applied"), Describe(caller));
            Assert.That(RaiseValue(1), Is.EqualTo(6), DescribeOutcomes(caller));
            return overrides;
        }

        private static string Describe(HotReloadOrchestratorResult result)
        {
            return DescribeOutcomes(result)
                + "\nReappliedSiblingPaths=" + string.Join(",", result.ReappliedSiblingPaths)
                + "\nWarnings:\n  " + string.Join("\n  ", result.Warnings);
        }

        private static string RemoveScaled(string source)
        {
            Assert.That(source, Does.Contain(ScaledDeclaration), "Precondition: the patched Scaled declaration must exist.");
            return source.Replace(ScaledDeclaration, string.Empty, StringComparison.Ordinal);
        }

        private static string BuildOwnerSource()
        {
            return
                "namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload\n"
                + "{\n"
                + "    public sealed class " + IntroducedTypeSimpleName + "\n"
                + "    {\n"
                + "        [System.Runtime.CompilerServices.MethodImpl(\n"
                + "            System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]\n"
                + "        public int Ping()\n"
                + "        {\n"
                + "            return 4;\n"
                + "        }\n"
                + "    }\n"
                + "}\n";
        }

        private static int RaiseValue(int value)
        {
            int received = 0;
            HotReloadBindingSplitRegistry registry = new HotReloadBindingSplitRegistry();
            registry.Register(payload => received = payload.Value);
            registry.Raise(value);
            return received;
        }

        private static Task<HotReloadOrchestratorResult> RunAsync(string[] files, Dictionary<string, string> overrides)
        {
            return HotReloadCompositionRoot.Services.Orchestrator.RunAsync(
                files,
                contentPathOverride: null,
                CancellationToken.None,
                new Dictionary<string, string>(overrides));
        }

        private static int CountKind(HotReloadOrchestratorResult result, HotReloadMethodOutcomeKind kind)
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

        private static int CountKindIn(HotReloadOrchestratorResult result, HotReloadMethodOutcomeKind kind, string fileName)
        {
            int count = 0;
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                if (outcome.Kind == kind && outcome.FilePath != null && outcome.FilePath.EndsWith(fileName, StringComparison.Ordinal))
                {
                    count++;
                }
            }

            return count;
        }

        private static string ReplaceOnce(string source, string anchor, string replacement)
        {
            Assert.That(source, Does.Contain(anchor), "Precondition: anchor must exist.");
            return source.Replace(anchor, replacement, StringComparison.Ordinal);
        }

        private static string ProjectRelativePath(string fileName)
        {
            return "Assets/Tests/Editor/HotReload/" + fileName;
        }
    }
}
