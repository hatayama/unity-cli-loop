using System.IO;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Verifies which closures and async bodies of a host the shim assembly cannot name still
    /// apply: the ones reaching only members the same reload adds run their new body, and the
    /// ones reaching a compiled member stay refused because no accessor can name the host.
    /// </summary>
    public sealed class HotReloadInternalHostClosureTests
    {
        private const string FixtureFileName = "HotReloadInternalHostFixtures.cs";
        private const string InternalHostReason = "containing type is not visible";

        private const string OriginalStaticCount =
            "[MethodImpl(MethodImplOptions.NoInlining)]\n"
            + "        internal static int Count(int threshold)\n"
            + "        {\n"
            + "            return -1;\n"
            + "        }\n";

        private const string OriginalInstanceCount =
            "[MethodImpl(MethodImplOptions.NoInlining)]\n"
            + "        internal int Count(int threshold)\n"
            + "        {\n"
            + "            return -1;\n"
            + "        }\n";

        private const string OriginalInstanceCountAsync =
            "[MethodImpl(MethodImplOptions.NoInlining)]\n"
            + "        internal Task<int> CountAsync(int threshold)\n"
            + "        {\n"
            + "            return Task.FromResult(-1);\n"
            + "        }\n";

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
        /// What: a lambda in an internal static class calling a private method the same reload
        /// adds is patched, and the new body runs through the closure.
        /// </summary>
        [Test]
        public async Task Run_LambdaCallingAddedStaticMethod_RunsTheNewBody()
        {
            HotReloadOrchestratorResult result = await RunEditedAsync(
                "InternalHostAddedStatic.cs",
                OriginalStaticCount,
                "[MethodImpl(MethodImplOptions.NoInlining)]\n"
                + "        internal static int Count(int threshold)\n"
                + "        {\n"
                + "            int[] values = { 1, 5, 9 };\n"
                + "            return values.Count(value => IsAbove(value, threshold));\n"
                + "        }\n\n"
                + "        private static bool IsAbove(int value, int threshold)\n"
                + "        {\n"
                + "            return value > threshold;\n"
                + "        }\n");

            AssertPatched(result, "Count");
            Assert.That(HotReloadInternalStaticHost.Count(4), Is.EqualTo(2), Describe(result));
        }

        /// <summary>
        /// What: a lambda capturing the receiver of an internal class to call an added instance
        /// method is patched, and that method receives the real receiver.
        /// </summary>
        [Test]
        public async Task Run_LambdaCapturingReceiverToCallAddedMethod_RunsTheNewBody()
        {
            HotReloadOrchestratorResult result = await RunEditedAsync(
                "InternalHostAddedInstance.cs",
                OriginalInstanceCount,
                "[MethodImpl(MethodImplOptions.NoInlining)]\n"
                + "        internal int Count(int threshold)\n"
                + "        {\n"
                + "            int[] values = { 1, 5, 9 };\n"
                + "            return values.Count(value => IsAbove(value, threshold));\n"
                + "        }\n\n"
                + "        private bool IsAbove(int value, int threshold)\n"
                + "        {\n"
                + "            return value > threshold && !ReferenceEquals(this, null);\n"
                + "        }\n");

            AssertPatched(result, "Count");
            HotReloadInternalInstanceHost host = new HotReloadInternalInstanceHost();
            Assert.That(host.Count(4), Is.EqualTo(2), Describe(result));
        }

        /// <summary>
        /// What: an added method called with a named argument from a lambda is patched, because
        /// the call reaches the shim with its argument list as written and no accessor delegate
        /// has to carry it.
        /// </summary>
        [Test]
        public async Task Run_LambdaCallingAddedMethodWithNamedArgument_RunsTheNewBody()
        {
            HotReloadOrchestratorResult result = await RunEditedAsync(
                "InternalHostAddedNamedArgument.cs",
                OriginalStaticCount,
                "[MethodImpl(MethodImplOptions.NoInlining)]\n"
                + "        internal static int Count(int threshold)\n"
                + "        {\n"
                + "            int[] values = { 1, 5, 9 };\n"
                + "            return values.Count(value => IsAbove(value, limit: threshold));\n"
                + "        }\n\n"
                + "        private static bool IsAbove(int value, int limit)\n"
                + "        {\n"
                + "            return value > limit;\n"
                + "        }\n");

            AssertPatched(result, "Count");
            Assert.That(HotReloadInternalStaticHost.Count(4), Is.EqualTo(2), Describe(result));
        }

        /// <summary>
        /// What: an async body of an internal class calling only an added method is patched and
        /// runs, even though its state machine JITs without skip-visibility.
        /// </summary>
        [Test]
        public async Task Run_AsyncBodyCallingAddedMethod_RunsTheNewBody()
        {
            HotReloadOrchestratorResult result = await RunEditedAsync(
                "InternalHostAddedAsync.cs",
                OriginalInstanceCountAsync,
                "[MethodImpl(MethodImplOptions.NoInlining)]\n"
                + "        internal async Task<int> CountAsync(int threshold)\n"
                + "        {\n"
                + "            int doubled = await Task.FromResult(Doubled(threshold));\n"
                + "            return doubled;\n"
                + "        }\n\n"
                + "        private int Doubled(int value)\n"
                + "        {\n"
                + "            return value * 2;\n"
                + "        }\n");

            AssertPatched(result, "CountAsync");
            HotReloadInternalInstanceHost host = new HotReloadInternalInstanceHost();
            Assert.That(await host.CountAsync(4), Is.EqualTo(8), Describe(result));
        }

        /// <summary>
        /// What: a lambda reaching a compiled member of the internal host stays skipped with the
        /// host's reason, and the method keeps its compiled body, so an added method beside it
        /// does not lift the refusal.
        /// </summary>
        [Test]
        public async Task Run_LambdaAlsoReadingCompiledMember_StaysSkippedWithTheHostReason()
        {
            HotReloadOrchestratorResult result = await RunEditedAsync(
                "InternalHostCompiledMember.cs",
                OriginalInstanceCount,
                "[MethodImpl(MethodImplOptions.NoInlining)]\n"
                + "        internal int Count(int threshold)\n"
                + "        {\n"
                + "            int[] values = { 1, 5, 9 };\n"
                + "            return values.Count(value => IsAbove(value, threshold) && Offset > 0);\n"
                + "        }\n\n"
                + "        private bool IsAbove(int value, int threshold)\n"
                + "        {\n"
                + "            return value > threshold;\n"
                + "        }\n");

            string reason = FindSkippedReason(result, "Count");
            Assert.That(reason, Does.Contain(InternalHostReason), Describe(result));
            HotReloadInternalInstanceHost host = new HotReloadInternalInstanceHost();
            Assert.That(host.Count(4), Is.EqualTo(-1), Describe(result));
        }

        /// <summary>
        /// What: an added method reading a compiled member of the internal host stays skipped with
        /// the host's reason, because its whole body JITs without skip-visibility, and the method
        /// calling it keeps its compiled body.
        /// </summary>
        [Test]
        public async Task Run_AddedMethodReadingCompiledMember_StaysSkippedWithTheHostReason()
        {
            HotReloadOrchestratorResult result = await RunEditedAsync(
                "InternalHostAddedReadsCompiled.cs",
                OriginalInstanceCount,
                "[MethodImpl(MethodImplOptions.NoInlining)]\n"
                + "        internal int Count(int threshold)\n"
                + "        {\n"
                + "            int[] values = { 1, 5, 9 };\n"
                + "            return values.Count(value => IsAbove(value, threshold));\n"
                + "        }\n\n"
                + "        private bool IsAbove(int value, int threshold)\n"
                + "        {\n"
                + "            return value + Offset > threshold;\n"
                + "        }\n");

            string reason = FindSkippedReason(result, "IsAbove");
            Assert.That(reason, Does.Contain(InternalHostReason), Describe(result));
            HotReloadInternalInstanceHost host = new HotReloadInternalInstanceHost();
            Assert.That(host.Count(4), Is.EqualTo(-1), Describe(result));
        }

        /// <summary>
        /// What: a compiled member read outside the lambda does not refuse the method, because the
        /// transplanted outer body runs with skip-visibility and only the lambda has to avoid it.
        /// </summary>
        [Test]
        public async Task Run_CompiledMemberCopiedToLocalOutsideLambda_RunsTheNewBody()
        {
            HotReloadOrchestratorResult result = await RunEditedAsync(
                "InternalHostCompiledMemberOutsideLambda.cs",
                OriginalInstanceCount,
                "[MethodImpl(MethodImplOptions.NoInlining)]\n"
                + "        internal int Count(int threshold)\n"
                + "        {\n"
                + "            int offset = Offset;\n"
                + "            int[] values = { 1, 5, 9 };\n"
                + "            return values.Count(value => value + offset > threshold);\n"
                + "        }\n");

            AssertPatched(result, "Count");
            HotReloadInternalInstanceHost host = new HotReloadInternalInstanceHost();
            Assert.That(host.Count(14), Is.EqualTo(2), Describe(result));
        }

        /// <summary>
        /// What: a lambda reaching an internal member of another, public type stays skipped with
        /// the host's reason, so the allowance covers an empty plan and not every accessor whose
        /// own signature happens to be visible.
        /// </summary>
        [Test]
        public async Task Run_LambdaReadingInternalMemberOfAnotherType_StaysSkippedWithTheHostReason()
        {
            HotReloadOrchestratorResult result = await RunEditedAsync(
                "InternalHostOtherTypeMember.cs",
                OriginalInstanceCount,
                "[MethodImpl(MethodImplOptions.NoInlining)]\n"
                + "        internal int Count(int threshold)\n"
                + "        {\n"
                + "            HotReloadInternalHostVisibleNeighbor other = new HotReloadInternalHostVisibleNeighbor();\n"
                + "            int[] values = { 1, 5, 9 };\n"
                + "            return values.Count(value => value + other.Hidden > threshold);\n"
                + "        }\n");

            string reason = FindSkippedReason(result, "Count");
            Assert.That(reason, Does.Contain(InternalHostReason), Describe(result));
            HotReloadInternalInstanceHost host = new HotReloadInternalInstanceHost();
            Assert.That(host.Count(4), Is.EqualTo(-1), Describe(result));
        }

        private static async Task<HotReloadOrchestratorResult> RunEditedAsync(
            string editedFileName,
            string original,
            string replacement)
        {
            string fixturePath = ResolveFixturePath();
            string fixture = File.ReadAllText(fixturePath).Replace("\r\n", "\n");
            Assert.That(fixture, Does.Contain(original), "Precondition: the edited member must exist.");
            string edited = fixture.Replace(original, replacement);
            return await HotReloadCompositionRoot.Services.Orchestrator.RunAsync(
                new[] { fixturePath },
                HotReloadTestSourceWriter.WriteEditedSource(editedFileName, edited),
                CancellationToken.None);
        }

        private static string ResolveFixturePath()
        {
            string path = Path.Combine(
                Application.dataPath,
                "Tests",
                "Editor",
                "HotReload",
                FixtureFileName);
            Assert.That(File.Exists(path), Is.True, "Fixture source missing: " + path);
            return Path.GetFullPath(path);
        }

        private static void AssertPatched(HotReloadOrchestratorResult result, string methodName)
        {
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                if (outcome.Kind == HotReloadMethodOutcomeKind.Patched
                    && outcome.Method.Contains("." + methodName + "("))
                {
                    return;
                }
            }

            Assert.Fail("Expected a Patched outcome for " + methodName + ".\n" + Describe(result));
        }

        private static string FindSkippedReason(HotReloadOrchestratorResult result, string methodName)
        {
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                if (outcome.Kind == HotReloadMethodOutcomeKind.Skipped
                    && outcome.Method.Contains("." + methodName + "(")
                    && outcome.Reason != null)
                {
                    return outcome.Reason;
                }
            }

            Assert.Fail("Expected a Skipped outcome for " + methodName + ".\n" + Describe(result));
            return null;
        }

        private static string Describe(HotReloadOrchestratorResult result)
        {
            string description = string.Empty;
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                description += outcome.Kind + " " + outcome.Method + " " + outcome.Reason + "\n";
            }

            return description;
        }
    }
}
