using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// End-to-end EditMode coverage for a Unity message a hot reload added to a compiled
    /// MonoBehaviour: what the run says about it, and whether the proxy that carries it reaches
    /// the instances in the open scene.
    /// </summary>
    /// <remarks>
    /// Why the installed forwarding rather than one the test builds: the run and the revert are
    /// what bind and clear it in production, and a forwarding of the test's own would keep passing
    /// after those calls were deleted. Only the Play Mode the attacher reads is substituted, since
    /// nothing attaches outside Play Mode and an EditMode test has to say it is playing.
    /// </remarks>
    public class HotReloadAddedUnityMessageE2ETests
    {
        private const string FixtureFileName = "HotReloadAddedUnityMessageE2EFixtures.cs";

        private readonly List<GameObject> _created = new List<GameObject>();
        private HotReloadDomainTestScope _scope;
        private IDisposable _playing;
        private HotReloadStubPlayModeQuery _playMode;

        [SetUp]
        public void SetUp()
        {
            _scope = new HotReloadDomainTestScope();
            _playMode = new HotReloadStubPlayModeQuery { IsPlaying = true };
            _playing = HotReloadServicesTestScope.BeginWithPlayModeQuery(_playMode);
        }

        [TearDown]
        public void TearDown()
        {
            // The proxies belong to the graph this scope installed, so they come off before it is
            // put back; the domain scope only knows about the forwarding of its own graph.
            Forwarding.Clear();
            foreach (GameObject gameObject in _created)
            {
                if (gameObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(gameObject);
                }
            }

            _created.Clear();
            _playing.Dispose();
            _scope.Dispose();
            VibeLogger.ClearMemoryLogs();
        }

        private static HotReloadUnityMessageForwarding Forwarding =>
            HotReloadCompositionRoot.Services.UnityMessageForwarding;

        /// <summary>
        /// What: adding Update to a compiled MonoBehaviour reports the method as added and says it
        /// is forwarded, without the warning that names messages a compile is needed for.
        /// </summary>
        [Test]
        public async Task Run_AddedUpdate_ReportsItAsForwardedWithoutTheCompileWarning()
        {
            HotReloadOrchestratorResult result = await RunWithAddedMemberAsync(
                "AddedUnityMessageUpdate.cs",
                IncrementUpdateSource());

            HotReloadMethodOutcome added = FindAdded(result, "Update");
            Assert.That(added.LifecycleNote, Is.EqualTo(HotReloadUnityMessageNotes.Forwarded));
            Assert.That(added.LifecycleNote, Does.Not.Contain("runs once on each existing instance"));
            Assert.That(result.Warnings, Has.None.Contain("not invoked until"));
        }

        /// <summary>
        /// What: an added Start carries the note that says it runs once when the proxy attaches,
        /// the one sentence the note on every other forwarded message leaves out.
        /// </summary>
        [Test]
        public async Task Run_AddedStart_ReportsTheNoteThatSaysItRunsWhenTheProxyAttaches()
        {
            HotReloadOrchestratorResult result = await RunWithAddedMemberAsync(
                "AddedUnityMessageStartNote.cs",
                "        private void Start()\n        {\n            Counter++;\n        }");

            HotReloadMethodOutcome added = FindAdded(result, "Start");
            Assert.That(added.LifecycleNote, Is.EqualTo(HotReloadUnityMessageNotes.ForwardedStart));
            Assert.That(added.LifecycleNote, Does.Contain("runs once on each existing instance"));
        }

        /// <summary>
        /// What: after the run, a tick puts one proxy on the instance already in the scene, and the
        /// message the proxy receives reaches the added method on that instance.
        /// </summary>
        [Test]
        public async Task Tick_AfterAddedUpdate_ForwardsTheMessageToTheLiveInstance()
        {
            HotReloadAddedUnityMessageFixture target = CreateFixture();
            await RunWithAddedMemberAsync("AddedUnityMessageUpdate.cs", IncrementUpdateSource());

            Forwarding.Tick();

            HotReloadUnityMessageProxy[] proxies =
                target.gameObject.GetComponents<HotReloadUnityMessageProxy>();
            Assert.That(proxies.Length, Is.EqualTo(1));
            InvokeMessage(proxies[0], "Update");
            Assert.That(target.Counter, Is.EqualTo(1));
        }

        /// <summary>
        /// What: re-applying the same method rebuilds the proxy around the new body, which only
        /// holds because a binding is compared by the shim it was built from and not by the method
        /// key that stayed the same.
        /// </summary>
        [Test]
        public async Task Tick_AfterTheAddedUpdateWasEditedAgain_ForwardsTheNewBody()
        {
            HotReloadAddedUnityMessageFixture target = CreateFixture();
            await RunWithAddedMemberAsync("AddedUnityMessageUpdate.cs", IncrementUpdateSource());
            Forwarding.Tick();
            InvokeMessage(SingleProxyOn(target), "Update");

            await RunWithAddedMemberAsync(
                "AddedUnityMessageUpdateAgain.cs",
                "        private void Update()\n        {\n            Counter += 10;\n        }");
            Forwarding.Tick();
            InvokeMessage(SingleProxyOn(target), "Update");

            Assert.That(target.Counter, Is.EqualTo(11));
        }

        /// <summary>
        /// What: a second run that applies the same added Start again with a new body keeps the
        /// proxy component already on the instance, so no second AddComponent reruns Start. An
        /// EditMode test never sees Unity call Start, so the component's identity is what shows it.
        /// </summary>
        [Test]
        public async Task Tick_AfterTheAddedStartWasAppliedAgain_KeepsTheAttachedProxy()
        {
            HotReloadAddedUnityMessageFixture target = CreateFixture();
            await RunWithAddedMemberAsync(
                "AddedUnityMessageStart.cs",
                "        private void Start()\n        {\n            Counter++;\n        }");
            Forwarding.Tick();
            HotReloadUnityMessageProxy first = SingleProxyOn(target);

            await RunWithAddedMemberAsync(
                "AddedUnityMessageStartAgain.cs",
                "        private void Start()\n        {\n            Counter += 10;\n        }");
            Forwarding.Tick();

            HotReloadUnityMessageProxy second = SingleProxyOn(target);
            Assert.That(second, Is.SameAs(first));
            InvokeMessage(second, "Start");
            Assert.That(target.Counter, Is.EqualTo(10));
        }

        /// <summary>
        /// What: in Play Mode, a run that deactivates an added Update a proxy was forwarding
        /// appends to the deactivation warning the sentence that says Unity stops invoking it.
        /// </summary>
        [Test]
        public async Task Run_DeactivatingAForwardedUpdateInPlayMode_SaysUnityStopsInvokingIt()
        {
            await RunWithAddedMemberAsync("AddedUnityMessageUpdateActive.cs", IncrementUpdateSource());

            HotReloadOrchestratorResult result = await RunWithAddedMemberAsync(
                "AddedUnityMessageUpdateUnbound.cs",
                UnboundUpdateSource());

            string warning = FindDeactivatedAddedMembersWarning(result);
            Assert.That(warning, Does.Contain("Update"));
            Assert.That(warning, Does.Contain("Unity no longer invokes the deactivated Unity message(s)"));
        }

        /// <summary>
        /// What: outside Play Mode no proxy carries the message, so the same deactivation warning
        /// leaves out the sentence about live instances.
        /// </summary>
        [Test]
        public async Task Run_DeactivatingAnAddedUpdateOutsidePlayMode_OmitsTheLiveInstanceSentence()
        {
            _playMode.IsPlaying = false;
            await RunWithAddedMemberAsync("AddedUnityMessageUpdateEditMode.cs", IncrementUpdateSource());

            HotReloadOrchestratorResult result = await RunWithAddedMemberAsync(
                "AddedUnityMessageUpdateEditModeUnbound.cs",
                UnboundUpdateSource());

            string warning = FindDeactivatedAddedMembersWarning(result);
            Assert.That(warning, Does.Contain("Update"));
            Assert.That(warning, Does.Not.Contain("Unity no longer invokes"));
        }

        /// <summary>
        /// What: a message this feature leaves to the compiler is reported as such on its own row
        /// and once for the run, and no proxy is attached for it.
        /// </summary>
        [Test]
        public async Task Run_AddedAwake_ReportsThatACompileIsNeededAndAttachesNoProxy()
        {
            HotReloadAddedUnityMessageFixture target = CreateFixture();

            HotReloadOrchestratorResult result = await RunWithAddedMemberAsync(
                "AddedUnityMessageAwake.cs",
                "        private void Awake()\n        {\n        }");
            Forwarding.Tick();

            HotReloadMethodOutcome added = FindAdded(result, "Awake");
            Assert.That(added.LifecycleNote, Is.EqualTo(HotReloadUnityMessageNotes.NotForwarded));
            Assert.That(
                result.Warnings,
                Has.Exactly(1).Contains("Added Unity messages not invoked until 'uloop compile':"));
            Assert.That(
                target.gameObject.GetComponents<HotReloadUnityMessageProxy>(), Is.Empty);
        }

        /// <summary>
        /// What: reverting everything takes the proxy off at the next tick on its own, so a caller
        /// that only reverts never leaves a component behind.
        /// </summary>
        [Test]
        public async Task Tick_AfterEverythingWasReverted_TakesTheProxyOff()
        {
            HotReloadAddedUnityMessageFixture target = CreateFixture();
            await RunWithAddedMemberAsync("AddedUnityMessageUpdate.cs", IncrementUpdateSource());
            Forwarding.Tick();
            Assert.That(
                target.gameObject.GetComponents<HotReloadUnityMessageProxy>().Length,
                Is.EqualTo(1),
                "Arrange: the tick must have attached the proxy.");

            HotReloadCompositionRoot.Services.Patcher.RevertAll();
            Forwarding.Tick();

            Assert.That(
                target.gameObject.GetComponents<HotReloadUnityMessageProxy>(), Is.Empty);
        }

        /// <summary>
        /// What: a proxy the run cannot build is reported by that run, which only holds because the
        /// run reconciles into its own warnings; the editor update reconciles with nowhere to report.
        /// </summary>
        [Test]
        public async Task Run_WhenTheProxyCannotBeBuilt_ReportsItAsAWarningOfThatRun()
        {
            HotReloadOrchestratorResult result = await RunWithAddedMemberAsync(
                "AddedUnityMessageUpdateTwice.cs",
                IncrementUpdateSource()
                    + "\n\n        private void Update(int steps)\n        {\n"
                    + "            Counter += steps;\n        }");

            Assert.That(result.Warnings, Has.Exactly(1).Contains("Unity message forwarding for"));
        }

        /// <summary>
        /// What: reverting through the tool takes the proxies off in the same step, so a caller
        /// that reverts and stops never leaves a component on the instances.
        /// </summary>
        [Test]
        public async Task RevertAll_ThroughTheTool_TakesTheProxyOffWithoutAnotherTick()
        {
            HotReloadAddedUnityMessageFixture target = CreateFixture();
            await RunWithAddedMemberAsync("AddedUnityMessageUpdate.cs", IncrementUpdateSource());
            Forwarding.Tick();
            Assert.That(
                target.gameObject.GetComponents<HotReloadUnityMessageProxy>().Length,
                Is.EqualTo(1),
                "Arrange: the tick must have attached the proxy.");

            HotReloadCompositionRoot.Services.StatusExecutor.ExecuteRevertAll();

            Assert.That(
                target.gameObject.GetComponents<HotReloadUnityMessageProxy>(), Is.Empty);
        }

        private static string IncrementUpdateSource()
        {
            return "        private void Update()\n        {\n            Counter++;\n        }";
        }

        // Why an unbound body: the worker refuses it before any shim is compiled while the method
        // is still declared, which is the shape the deactivation warning reports.
        private static string UnboundUpdateSource()
        {
            return "        private void Update()\n        {\n            MissingHelperAddedByEdit();\n        }";
        }

        private static string FindDeactivatedAddedMembersWarning(HotReloadOrchestratorResult result)
        {
            string format = HotReloadConstants.DeactivatedAddedMembersWarningFormat;
            string prefix = format.Substring(0, format.IndexOf("{0}", StringComparison.Ordinal));
            foreach (string warning in result.Warnings)
            {
                if (warning.StartsWith(prefix, StringComparison.Ordinal))
                {
                    return warning;
                }
            }

            Assert.Fail("Expected the deactivated added members warning.\n" + string.Join("\n", result.Warnings));
            return null;
        }

        private async Task<HotReloadOrchestratorResult> RunWithAddedMemberAsync(
            string editedFileName,
            string memberText)
        {
            string fixturePath = FixturePath();
            string editedPath = HotReloadTestSourceWriter.WriteEditedSource(
                editedFileName,
                InsertMember(memberText));
            HotReloadOrchestratorResult result =
                await HotReloadCompositionRoot.Services.Orchestrator.RunAsync(
                    new[] { fixturePath },
                    contentPathOverride: editedPath,
                    CancellationToken.None);
            AssertNoFailure(result);
            return result;
        }

        private static string InsertMember(string memberText)
        {
            string source = File.ReadAllText(FixturePath());
            Assert.That(
                source,
                Does.Contain(HotReloadAddedUnityMessageE2EFixtures.CounterFieldDeclaration),
                "Precondition: the fixture's counter field must exist.");
            return source.Replace(
                HotReloadAddedUnityMessageE2EFixtures.CounterFieldDeclaration,
                HotReloadAddedUnityMessageE2EFixtures.CounterFieldDeclaration + "\n\n" + memberText,
                StringComparison.Ordinal);
        }

        private static string FixturePath()
        {
            string path = Path.GetFullPath(
                Path.Combine(Application.dataPath, "Tests", "Editor", "HotReload", FixtureFileName));
            Assert.That(File.Exists(path), Is.True, "Fixture missing: " + path);
            return path;
        }

        private static HotReloadUnityMessageProxy SingleProxyOn(
            HotReloadAddedUnityMessageFixture target)
        {
            HotReloadUnityMessageProxy[] proxies =
                target.gameObject.GetComponents<HotReloadUnityMessageProxy>();
            Assert.That(proxies.Length, Is.EqualTo(1));
            return proxies[0];
        }

        private static void InvokeMessage(HotReloadUnityMessageProxy proxy, string messageName)
        {
            MethodInfo message = proxy.GetType().GetMethod(
                messageName,
                BindingFlags.Public | BindingFlags.Instance);
            Assert.That(message, Is.Not.Null, "The generated proxy must declare " + messageName + ".");
            message.Invoke(proxy, null);
        }

        private HotReloadAddedUnityMessageFixture CreateFixture()
        {
            GameObject owner = new GameObject("AddedUnityMessageE2ETests_Target");
            _created.Add(owner);
            HotReloadAddedUnityMessageFixture component =
                owner.AddComponent<HotReloadAddedUnityMessageFixture>();
            Assert.That(component, Is.Not.Null, "Arrange: the fixture component must be addable.");
            return component;
        }

        private static HotReloadMethodOutcome FindAdded(
            HotReloadOrchestratorResult result,
            string methodNamePart)
        {
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                if (outcome.Kind == HotReloadMethodOutcomeKind.Added
                    && outcome.Method != null
                    && outcome.Method.Contains(methodNamePart))
                {
                    return outcome;
                }
            }

            Assert.Fail("Expected an added " + methodNamePart + ".\n" + FormatOutcomes(result));
            return null;
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

        private static string FormatOutcomes(HotReloadOrchestratorResult result)
        {
            List<string> lines = new List<string>();
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                lines.Add(outcome.Kind + " " + outcome.Method + " :: " + outcome.Reason);
            }

            return string.Join("\n", lines);
        }
    }
}
