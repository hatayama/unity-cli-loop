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
    /// End-to-end EditMode coverage for a field-like event the edit adds to a compiled class: its
    /// delegate lives in the added-field store, so patched bodies of the declaring type and of
    /// other types in the same run can subscribe, unsubscribe, and raise it.
    /// </summary>
    public class HotReloadAddedEventE2ETests
    {
        private const string PublisherFileName = "HotReloadAddedEventApplyPublisher.cs";
        private const string SubscriberFileName = "HotReloadAddedEventApplySubscriber.cs";
        private const string ExistingEventAnchor = "        public event Action<int> Existing;";
        private const string RaiseBodyAnchor = "            Existing?.Invoke(value);";
        private const string RaiseStaticAnchor =
            "        public static void RaiseStatic(int value)\n        {\n        }";
        private const string WireAnchor =
            "        public void Wire(HotReloadAddedEventApplyPublisher publisher)\n        {\n        }";
        private const string UnwireAnchor =
            "        public void Unwire(HotReloadAddedEventApplyPublisher publisher)\n        {\n        }";
        private const string WireStaticAnchor = "        public void WireStatic()\n        {\n        }";
        private const string AddedEvent = "        public event Action<int> Changed;";
        private const string RaiseChanged = "            Changed?.Invoke(value);";

        // The sentence only the declared-type warning carries, so another warning naming the
        // event cannot satisfy the pin.
        private const string DeclaredTypeChangedToken = "with a different type";

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
        /// What: a patched method of the declaring type subscribes a lambda to the added event and
        /// raises it, and the lambda runs.
        /// </summary>
        [Test]
        public async Task Run_AddedInstanceEvent_RaisedFromPatchedMethod_InvokesSubscribers()
        {
            string publisher = EditPublisher(
                AddedEvent,
                "            if (Changed == null)\n            {\n"
                + "                Changed += forwarded => Existing?.Invoke(forwarded * 10);\n            }\n\n"
                + RaiseChanged);

            HotReloadOrchestratorResult result = await RunAsync(publisher, ReadFixture(SubscriberFileName));

            AssertPatched(result, ".Raise(");
            HotReloadAddedEventApplyPublisher target = new HotReloadAddedEventApplyPublisher();
            HotReloadAddedEventApplySubscriber subscriber = new HotReloadAddedEventApplySubscriber();
            target.Existing += subscriber.Accept;
            target.Raise(3);
            Assert.That(subscriber.Received, Is.EqualTo(30), FormatOutcomes(result));
        }

        /// <summary>
        /// What: a patched method of another type in the same run subscribes a lambda to the added
        /// event, and a raise from the declaring type reaches it.
        /// </summary>
        [Test]
        public async Task Run_OtherTypeSubscribesLambda_ReceivesTheRaise()
        {
            HotReloadOrchestratorResult result = await RunAsync(
                EditPublisher(AddedEvent, RaiseChanged),
                EditSubscriber(WireAnchor, "publisher.Changed += value => Accept(value);"));

            AssertPatched(result, ".Raise(");
            AssertPatched(result, ".Wire(");
            Assert.That(WireAndRaise(4), Is.EqualTo(4), FormatOutcomes(result));
        }

        /// <summary>
        /// What: unsubscribing the same compiled method group removes it, so a later raise no
        /// longer reaches the subscriber.
        /// </summary>
        [Test]
        public async Task Run_UnsubscribeWithTheSameDelegate_StopsDelivery()
        {
            string subscriber = EditSubscriber(WireAnchor, "publisher.Changed += Accept;");
            subscriber = ReplaceMember(subscriber, UnwireAnchor, "publisher.Changed -= Accept;");
            HotReloadOrchestratorResult result = await RunAsync(EditPublisher(AddedEvent, RaiseChanged), subscriber);

            AssertPatched(result, ".Unwire(");
            HotReloadAddedEventApplyPublisher target = new HotReloadAddedEventApplyPublisher();
            HotReloadAddedEventApplySubscriber listener = new HotReloadAddedEventApplySubscriber();
            listener.Wire(target);
            target.Raise(2);
            listener.Unwire(target);
            target.Raise(5);
            Assert.That(listener.Received, Is.EqualTo(2), FormatOutcomes(result));
        }

        /// <summary>
        /// What: an added property's getter that calls a private member, and so goes through
        /// accessor delegates, raises the added event through the store and the raise arrives,
        /// instead of the run planning a backing field the compiled class does not have.
        /// </summary>
        [Test]
        public async Task Run_AddedPropertyGetterWithPrivateAccessRaisesAddedEvent_ReachesSubscriber()
        {
            string publisher = EditPublisher(
                AddedEvent + "\n\n        public int Fire\n        {\n"
                + "            get\n            {\n                Changed?.Invoke(Secret());\n"
                + "                return 1;\n            }\n        }",
                "            if (Changed == null)\n            {\n"
                + "                Changed += forwarded => Existing?.Invoke(forwarded);\n            }\n\n"
                + "            _ = Fire;");

            HotReloadOrchestratorResult result = await RunAsync(publisher, ReadFixture(SubscriberFileName));

            AssertPatched(result, ".Raise(");
            Assert.That(RaiseThroughExisting(), Is.EqualTo(2), FormatOutcomes(result));
        }

        /// <summary>
        /// What: a compiled property's getter edited to call a private member and raise the added
        /// event is patched, and the raise reaches a subscriber.
        /// </summary>
        [Test]
        public async Task Run_CompiledGetterWithPrivateAccessRaisesAddedEvent_ReachesSubscriber()
        {
            string publisher = EditPublisher(
                AddedEvent,
                "            if (Changed == null)\n            {\n"
                + "                Changed += forwarded => Existing?.Invoke(forwarded);\n            }\n\n"
                + "            _ = Probe;");
            publisher = ReplaceInSource(
                publisher,
                "            get { return 0; }",
                "            get\n            {\n                Changed?.Invoke(Secret());\n"
                + "                return 1;\n            }");

            HotReloadOrchestratorResult result = await RunAsync(publisher, ReadFixture(SubscriberFileName));

            AssertPatched(result, ".Raise(");
            Assert.That(RaiseThroughExisting(), Is.EqualTo(2), FormatOutcomes(result));
        }

        /// <summary>
        /// What: another type subscribing to a compiled event through parentheses,
        /// '(publisher.Existing) += h', is patched and the compiled raise reaches the handler,
        /// so looking past the parentheses does not break the compiled-event subscription path.
        /// </summary>
        [Test]
        public async Task Run_OtherTypeSubscribesToCompiledEventThroughParentheses_IsPatched()
        {
            HotReloadOrchestratorResult result = await RunAsync(
                ReadFixture(PublisherFileName),
                EditSubscriber(WireAnchor, "(publisher.Existing) += Accept;"));

            AssertPatched(result, ".Wire(");
            Assert.That(WireAndRaise(4), Is.EqualTo(4), FormatOutcomes(result));
        }

        /// <summary>
        /// What: the declaring type subscribing to its compiled event through parentheses,
        /// '(Existing) += h', is patched and the raise reaches both that handler and a handler
        /// compiled code subscribed.
        /// </summary>
        [Test]
        public async Task Run_DeclaringTypeSubscribesToCompiledEventThroughParentheses_IsPatched()
        {
            string publisher = ReplaceInSource(
                ReadFixture(PublisherFileName),
                RaiseBodyAnchor,
                "            (Existing) += forwarded => LastForwarded = forwarded * 10;\n" + RaiseBodyAnchor);
            HotReloadAddedEventApplyPublisher.LastForwarded = 0;

            HotReloadOrchestratorResult result = await RunAsync(publisher, ReadFixture(SubscriberFileName));

            AssertPatched(result, ".Raise(");
            HotReloadAddedEventApplyPublisher target = new HotReloadAddedEventApplyPublisher();
            HotReloadAddedEventApplySubscriber subscriber = new HotReloadAddedEventApplySubscriber();
            target.Existing += subscriber.Accept;
            target.Raise(3);
            Assert.That(HotReloadAddedEventApplyPublisher.LastForwarded, Is.EqualTo(30), FormatOutcomes(result));
            Assert.That(subscriber.Received, Is.EqualTo(3), FormatOutcomes(result));
        }

        /// <summary>
        /// What: an added static event with a generic delegate type is raised from a patched static
        /// method and reaches a lambda another type subscribed.
        /// </summary>
        [Test]
        public async Task Run_AddedStaticGenericEvent_RaiseAndSubscribe()
        {
            string publisher = ReplaceInSource(
                ReadFixture(PublisherFileName),
                ExistingEventAnchor,
                ExistingEventAnchor + "\n\n        public static event Action<int[]> LinesCleared;");
            publisher = ReplaceInSource(
                publisher,
                RaiseStaticAnchor,
                "        public static void RaiseStatic(int value)\n        {\n"
                + "            LinesCleared?.Invoke(new[] { value, value, value });\n        }");
            string subscriber = ReplaceMember(
                ReadFixture(SubscriberFileName),
                WireStaticAnchor,
                "HotReloadAddedEventApplyPublisher.LinesCleared += lines => Accept(lines.Length);");

            HotReloadOrchestratorResult result = await RunAsync(publisher, subscriber);

            AssertPatched(result, ".RaiseStatic(");
            AssertPatched(result, ".WireStatic(");
            HotReloadAddedEventApplySubscriber listener = new HotReloadAddedEventApplySubscriber();
            listener.WireStatic();
            HotReloadAddedEventApplyPublisher.RaiseStatic(7);
            Assert.That(listener.Received, Is.EqualTo(3), FormatOutcomes(result));
        }

        /// <summary>
        /// What: the subscriber's file passed before the publisher's still binds, because whether
        /// an added event lives in the store does not depend on which file was classified first.
        /// </summary>
        [Test]
        public async Task Run_SubscriberFileFirst_StillBinds()
        {
            string publisherPath = FixturePath(PublisherFileName);
            string subscriberPath = FixturePath(SubscriberFileName);
            HotReloadOrchestratorResult result = await HotReloadCompositionRoot.Services.Orchestrator.RunAsync(
                new[] { subscriberPath, publisherPath },
                contentPathOverride: null,
                CancellationToken.None,
                BuildOverrides(
                    EditPublisher(AddedEvent, RaiseChanged),
                    EditSubscriber(WireAnchor, "publisher.Changed += Accept;")));

            AssertPatched(result, ".Raise(");
            AssertPatched(result, ".Wire(");
            Assert.That(WireAndRaise(6), Is.EqualTo(6), FormatOutcomes(result));
        }

        /// <summary>
        /// What: a later run that leaves the added event as it was and edits only the raising body
        /// keeps the subscribers the first run stored, because the event is still absent from the
        /// compiled class and keeps the same store key.
        /// </summary>
        [Test]
        public async Task Run_ReapplyUnchanged_KeepsSubscribers()
        {
            string publisher = EditPublisher(AddedEvent, RaiseChanged);
            string subscriber = EditSubscriber(WireAnchor, "publisher.Changed += Accept;");
            await RunAsync(publisher, subscriber);
            HotReloadAddedEventApplyPublisher target = new HotReloadAddedEventApplyPublisher();
            HotReloadAddedEventApplySubscriber listener = new HotReloadAddedEventApplySubscriber();
            listener.Wire(target);

            HotReloadOrchestratorResult second = await RunAsync(
                EditPublisher(AddedEvent, "            Changed?.Invoke(value + 1);"),
                subscriber);

            AssertPatched(second, ".Raise(");
            target.Raise(8);
            Assert.That(listener.Received, Is.EqualTo(9), FormatOutcomes(second));
            Assert.That(second.Warnings ?? new List<string>(), Has.None.Contains(DeclaredTypeChangedToken), FormatOutcomes(second));
        }

        /// <summary>
        /// What: changing the added event's delegate type between runs drops the stored
        /// subscribers, because the store resets a value of another type, and the run warns that
        /// it did, naming the event. A change of a generic argument alone counts as a change.
        /// </summary>
        [Test]
        public async Task Run_ChangedDelegateType_DropsSubscribersWithAWarning()
        {
            await RunAsync(
                EditPublisher(AddedEvent, RaiseChanged),
                EditSubscriber(WireAnchor, "publisher.Changed += Accept;"));
            HotReloadAddedEventApplyPublisher target = new HotReloadAddedEventApplyPublisher();
            HotReloadAddedEventApplySubscriber listener = new HotReloadAddedEventApplySubscriber();
            listener.Wire(target);

            HotReloadOrchestratorResult second = await RunAsync(
                EditPublisher("        public event Action<long> Changed;", RaiseChanged),
                EditSubscriber(WireAnchor, "publisher.Changed += value => Accept((int)value);"));

            AssertPatched(second, ".Raise(");
            target.Raise(9);
            Assert.That(listener.Received, Is.EqualTo(0), FormatOutcomes(second));
            string changedName = typeof(HotReloadAddedEventApplyPublisher).FullName + ".Changed";
            Assert.That(
                second.Warnings ?? new List<string>(),
                Has.Some.Matches<string>(
                    warning => warning.Contains(DeclaredTypeChangedToken) && warning.Contains(changedName)),
                FormatOutcomes(second));
        }

        /// <summary>
        /// What: removing the added event from the source drops it from the run's added fields,
        /// while its stored subscribers stay until a revert, as an added field's value does; adding
        /// the event back reaches the subscriber the first run stored.
        /// </summary>
        [Test]
        public async Task Run_RemovedAddedEvent_LeavesTheStoredValueLikeAField()
        {
            string publisher = EditPublisher(AddedEvent, RaiseChanged);
            string subscriber = EditSubscriber(WireAnchor, "publisher.Changed += Accept;");
            HotReloadOrchestratorResult first = await RunAsync(publisher, subscriber);
            Assert.That(first.AddedFields, Has.Some.EndsWith(".Changed"), FormatOutcomes(first));
            HotReloadAddedEventApplyPublisher target = new HotReloadAddedEventApplyPublisher();
            HotReloadAddedEventApplySubscriber listener = new HotReloadAddedEventApplySubscriber();
            listener.Wire(target);

            HotReloadOrchestratorResult second = await RunAsync(
                ReadFixture(PublisherFileName),
                ReadFixture(SubscriberFileName));
            Assert.That(second.AddedFields, Has.None.EndsWith(".Changed"), FormatOutcomes(second));

            HotReloadOrchestratorResult third = await RunAsync(publisher, subscriber);
            AssertPatched(third, ".Raise(");
            target.Raise(4);
            Assert.That(listener.Received, Is.EqualTo(4), FormatOutcomes(third));
        }

        private static int RaiseThroughExisting()
        {
            HotReloadAddedEventApplyPublisher target = new HotReloadAddedEventApplyPublisher();
            HotReloadAddedEventApplySubscriber listener = new HotReloadAddedEventApplySubscriber();
            target.Existing += listener.Accept;
            target.Raise(0);
            return listener.Received;
        }

        private static int WireAndRaise(int value)
        {
            HotReloadAddedEventApplyPublisher target = new HotReloadAddedEventApplyPublisher();
            HotReloadAddedEventApplySubscriber listener = new HotReloadAddedEventApplySubscriber();
            listener.Wire(target);
            target.Raise(value);
            return listener.Received;
        }

        private static string EditPublisher(string addedEvent, string raiseBody)
        {
            string publisher = ReplaceInSource(
                ReadFixture(PublisherFileName),
                ExistingEventAnchor,
                ExistingEventAnchor + "\n\n" + addedEvent);
            return ReplaceInSource(publisher, RaiseBodyAnchor, raiseBody);
        }

        private static string EditSubscriber(string anchor, string body)
        {
            return ReplaceMember(ReadFixture(SubscriberFileName), anchor, body);
        }

        // Replaces an empty-bodied member with the same signature and the given body.
        private static string ReplaceMember(string source, string anchor, string body)
        {
            string signature = anchor.Substring(0, anchor.IndexOf("\n", StringComparison.Ordinal));
            return ReplaceInSource(source, anchor, signature + "\n        {\n            " + body + "\n        }");
        }

        private static string ReplaceInSource(string source, string anchor, string replacement)
        {
            Assert.That(source, Does.Contain(anchor), "Precondition: anchor must exist: " + anchor);
            return source.Replace(anchor, replacement, StringComparison.Ordinal);
        }

        private static Task<HotReloadOrchestratorResult> RunAsync(string editedPublisher, string editedSubscriber)
        {
            return HotReloadCompositionRoot.Services.Orchestrator.RunAsync(
                new[] { FixturePath(PublisherFileName), FixturePath(SubscriberFileName) },
                contentPathOverride: null,
                CancellationToken.None,
                BuildOverrides(editedPublisher, editedSubscriber));
        }

        private static Dictionary<string, string> BuildOverrides(string editedPublisher, string editedSubscriber)
        {
            return new Dictionary<string, string>
            {
                [FixturePath(PublisherFileName)] = HotReloadTestSourceWriter.WriteEditedSource(
                    "AddedEventE2E_" + PublisherFileName,
                    editedPublisher),
                [FixturePath(SubscriberFileName)] = HotReloadTestSourceWriter.WriteEditedSource(
                    "AddedEventE2E_" + SubscriberFileName,
                    editedSubscriber)
            };
        }

        private static void AssertPatched(HotReloadOrchestratorResult result, string methodPart)
        {
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                if (outcome.Method != null && outcome.Method.Contains(methodPart))
                {
                    Assert.That(outcome.Kind, Is.EqualTo(HotReloadMethodOutcomeKind.Patched), FormatOutcomes(result));
                    return;
                }
            }

            Assert.Fail("Missing outcome for " + methodPart + ".\n" + FormatOutcomes(result));
        }

        private static string ReadFixture(string fileName)
        {
            return File.ReadAllText(FixturePath(fileName));
        }

        private static string FixturePath(string fileName)
        {
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "Tests", "Editor", "HotReload", fileName));
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
