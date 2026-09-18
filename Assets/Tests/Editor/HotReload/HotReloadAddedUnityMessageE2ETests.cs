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
    /// Why this builds its own forwarding instead of ticking the installed one: only a run in Play
    /// Mode attaches proxies, and an EditMode test has to say so itself. The forwarding here reads
    /// the same installed domain, so what a run wrote is what it reconciles.
    /// </remarks>
    public class HotReloadAddedUnityMessageE2ETests
    {
        private const string FixtureFileName = "HotReloadAddedUnityMessageE2EFixtures.cs";

        private readonly List<GameObject> _created = new List<GameObject>();
        private HotReloadDomainTestScope _scope;
        private HotReloadUnityMessageForwarding _forwarding;

        [SetUp]
        public void SetUp()
        {
            _scope = new HotReloadDomainTestScope();
            HotReloadUnityMessageProxyAttacher attacher =
                new HotReloadUnityMessageProxyAttacher(
                    new HotReloadStubPlayModeQuery { IsPlaying = true },
                    new HotReloadUnityMessageProxyTypeBuilder());
            _forwarding = new HotReloadUnityMessageForwarding(
                HotReloadCompositionRoot.Services.Domain,
                attacher);
        }

        [TearDown]
        public void TearDown()
        {
            _forwarding.Clear();
            foreach (GameObject gameObject in _created)
            {
                if (gameObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(gameObject);
                }
            }

            _created.Clear();
            _scope.Dispose();
            VibeLogger.ClearMemoryLogs();
        }

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
            Assert.That(result.Warnings, Has.None.Contain("not invoked until"));
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

            _forwarding.Tick();

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
            _forwarding.Tick();
            InvokeMessage(SingleProxyOn(target), "Update");

            await RunWithAddedMemberAsync(
                "AddedUnityMessageUpdateAgain.cs",
                "        private void Update()\n        {\n            Counter += 10;\n        }");
            _forwarding.Tick();
            InvokeMessage(SingleProxyOn(target), "Update");

            Assert.That(target.Counter, Is.EqualTo(11));
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
            _forwarding.Tick();

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
            _forwarding.Tick();
            Assert.That(
                target.gameObject.GetComponents<HotReloadUnityMessageProxy>().Length,
                Is.EqualTo(1),
                "Arrange: the tick must have attached the proxy.");

            HotReloadCompositionRoot.Services.Patcher.RevertAll();
            _forwarding.Tick();

            Assert.That(
                target.gameObject.GetComponents<HotReloadUnityMessageProxy>(), Is.Empty);
        }

        private static string IncrementUpdateSource()
        {
            return "        private void Update()\n        {\n            Counter++;\n        }";
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
