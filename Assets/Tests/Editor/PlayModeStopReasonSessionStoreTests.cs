using System;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Compilation;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Tests pending-priority and unknown fallback for Play Mode stop reasons.
    /// </summary>
    public sealed class PlayModeStopReasonSessionStoreTests
    {
        [SetUp]
        public void SetUp()
        {
            PlayModeStopReasonSessionStore.ClearForTests();
        }

        [TearDown]
        public void TearDown()
        {
            PlayModeStopReasonSessionStore.ClearForTests();
        }

        /// <summary>
        /// What: SetPending overwrites a previous pending reason, including script-compilation.
        /// </summary>
        [Test]
        public void SetPending_WhenCalledAfterTrySetPending_OverwritesScriptCompilation()
        {
            PlayModeStopReasonSessionStore.TrySetPending("script-compilation");
            PlayModeStopReasonSessionStore.SetPending("cli-control-play-mode");

            Assert.That(PlayModeStopReasonSessionStore.PendingReason, Is.EqualTo("cli-control-play-mode"));
        }

        /// <summary>
        /// What: TrySetPending leaves an explicit pending reason in place.
        /// </summary>
        [Test]
        public void TrySetPending_WhenPendingAlreadySet_DoesNotOverwrite()
        {
            PlayModeStopReasonSessionStore.SetPending("cli-compile-stop-setting");
            PlayModeStopReasonSessionStore.TrySetPending("script-compilation");

            Assert.That(PlayModeStopReasonSessionStore.PendingReason, Is.EqualTo("cli-compile-stop-setting"));
        }

        /// <summary>
        /// What: ConfirmPending with no pending reason stores unknown plus the given timestamp.
        /// </summary>
        [Test]
        public void ConfirmPending_WhenNoPendingReason_StoresUnknown()
        {
            PlayModeStopReasonSessionStore.ConfirmPending("2026-01-01T00:00:00.0000000Z");

            PlayModeStopReasonRecord record = PlayModeStopReasonSessionStore.TryReadConfirmed();
            Assert.That(record.HasValue, Is.True);
            Assert.That(record.StoppedBy, Is.EqualTo("unknown"));
            Assert.That(record.StoppedAtUtc, Is.EqualTo("2026-01-01T00:00:00.0000000Z"));
            Assert.That(PlayModeStopReasonSessionStore.PendingReason, Is.Null);
        }

        /// <summary>
        /// What: ConfirmPending writes the pending reason and clears pending.
        /// </summary>
        [Test]
        public void ConfirmPending_WhenPendingIsSet_StoresThatReason()
        {
            PlayModeStopReasonSessionStore.SetPending("cli-run-tests-cancel");
            PlayModeStopReasonSessionStore.ConfirmPending("2026-01-02T00:00:00.0000000Z");

            PlayModeStopReasonRecord record = PlayModeStopReasonSessionStore.TryReadConfirmed();
            Assert.That(record.StoppedBy, Is.EqualTo("cli-run-tests-cancel"));
            Assert.That(record.StoppedAtUtc, Is.EqualTo("2026-01-02T00:00:00.0000000Z"));
            Assert.That(PlayModeStopReasonSessionStore.PendingReason, Is.Null);
        }

        /// <summary>
        /// What: ConfirmPending writes the reason and timestamp into SessionState under the wire keys.
        /// </summary>
        [Test]
        public void ConfirmPending_WhenCalled_WritesLiteralSessionStateKeys()
        {
            PlayModeStopReasonSessionStore.SetPending("cli-control-play-mode");
            PlayModeStopReasonSessionStore.ConfirmPending("2026-01-05T00:00:00.0000000Z");

            Assert.That(
                SessionState.GetString("io.github.hatayama.uloopmcp.playModeStopReason.reason", string.Empty),
                Is.EqualTo("cli-control-play-mode"));
            Assert.That(
                SessionState.GetString(
                    "io.github.hatayama.uloopmcp.playModeStopReason.stoppedAtUtc",
                    string.Empty),
                Is.EqualTo("2026-01-05T00:00:00.0000000Z"));
        }

        /// <summary>
        /// What: TryReadConfirmed returns values previously stored under the SessionState wire keys.
        /// </summary>
        [Test]
        public void TryReadConfirmed_WhenSessionStateSeededWithLiteralKeys_ReturnsThoseValues()
        {
            SessionState.SetString(
                "io.github.hatayama.uloopmcp.playModeStopReason.reason",
                "script-compilation");
            SessionState.SetString(
                "io.github.hatayama.uloopmcp.playModeStopReason.stoppedAtUtc",
                "2026-01-06T00:00:00.0000000Z");

            PlayModeStopReasonRecord record = PlayModeStopReasonSessionStore.TryReadConfirmed();

            Assert.That(record.StoppedBy, Is.EqualTo("script-compilation"));
            Assert.That(record.StoppedAtUtc, Is.EqualTo("2026-01-06T00:00:00.0000000Z"));
        }
    }

    /// <summary>
    /// Tests that each Play Mode stop input path stamps the matching pending reason.
    /// </summary>
    public sealed class PlayModeStopReasonWiringTests
    {
        [SetUp]
        public void SetUp()
        {
            PlayModeStopReasonSessionStore.ClearForTests();
        }

        [TearDown]
        public void TearDown()
        {
            PlayModeStopReasonSessionStore.ClearForTests();
        }

        /// <summary>
        /// What: setting IsPlaying false on the editor state service stamps cli-control-play-mode.
        /// </summary>
        [Test]
        public void EditorStateService_WhenIsPlayingSetFalse_SetsPendingCliControlPlayMode()
        {
            ControlPlayModeEditorStateService service = new ControlPlayModeEditorStateService();

            service.IsPlaying = false;

            Assert.That(
                PlayModeStopReasonSessionStore.PendingReason,
                Is.EqualTo("cli-control-play-mode"));
        }

        /// <summary>
        /// What: compile StopPlayMode stamps cli-compile-stop-setting before exiting Play Mode.
        /// </summary>
        [Test]
        public void StopPlayMode_WhenInvoked_SetsPendingCliCompileStopSetting()
        {
            PlayModeCompilationPreparationService service = new PlayModeCompilationPreparationService();

            service.StopPlayMode();

            Assert.That(
                PlayModeStopReasonSessionStore.PendingReason,
                Is.EqualTo("cli-compile-stop-setting"));
        }

        /// <summary>
        /// What: run-tests cancel exit stamps cli-run-tests-cancel.
        /// </summary>
        [Test]
        public void StopPlayingForCancel_WhenInvoked_SetsPendingCliRunTestsCancel()
        {
            RunTestsCancelStopRestoreUnityHooks.StopPlayingForCancel();

            Assert.That(
                PlayModeStopReasonSessionStore.PendingReason,
                Is.EqualTo("cli-run-tests-cancel"));
        }

        /// <summary>
        /// What: compilationStarted stamps script-compilation only when pending is empty.
        /// </summary>
        [Test]
        public void HandleCompilationStarted_WhenPendingEmpty_SetsScriptCompilation()
        {
            PlayModeStopReasonSubscriber.HandleCompilationStarted(null);

            Assert.That(
                PlayModeStopReasonSessionStore.PendingReason,
                Is.EqualTo("script-compilation"));
        }

        /// <summary>
        /// What: compilationStarted does not replace an explicit pending reason.
        /// </summary>
        [Test]
        public void HandleCompilationStarted_WhenPendingAlreadySet_DoesNotOverwrite()
        {
            PlayModeStopReasonSessionStore.SetPending("cli-control-play-mode");

            PlayModeStopReasonSubscriber.HandleCompilationStarted(null);

            Assert.That(
                PlayModeStopReasonSessionStore.PendingReason,
                Is.EqualTo("cli-control-play-mode"));
        }

        /// <summary>
        /// What: ExitingPlayMode with no pending confirms unknown.
        /// </summary>
        [Test]
        public void HandlePlayModeStateChanged_WhenExitingPlayModeWithoutPending_ConfirmsUnknown()
        {
            PlayModeStopReasonSubscriber.HandlePlayModeStateChanged(PlayModeStateChange.ExitingPlayMode);

            PlayModeStopReasonRecord record = PlayModeStopReasonSessionStore.TryReadConfirmed();
            Assert.That(record.StoppedBy, Is.EqualTo("unknown"));
            Assert.That(
                Regex.IsMatch(
                    record.StoppedAtUtc,
                    "^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}\\.[0-9]{7}Z$"),
                Is.True,
                record.StoppedAtUtc);
            Assert.That(PlayModeStopReasonSessionStore.PendingReason, Is.Null);
        }

        /// <summary>
        /// What: compilationFinished clears a leftover script-compilation fallback pending.
        /// </summary>
        [Test]
        public void HandleCompilationFinished_WhenPendingIsScriptCompilation_ClearsPending()
        {
            PlayModeStopReasonSessionStore.TrySetPending("script-compilation");

            PlayModeStopReasonSubscriber.HandleCompilationFinished(null);

            Assert.That(PlayModeStopReasonSessionStore.PendingReason, Is.Null);
        }

        /// <summary>
        /// What: compilationFinished leaves an explicit CLI pending reason in place.
        /// </summary>
        [Test]
        public void HandleCompilationFinished_WhenPendingIsExplicitReason_LeavesPending()
        {
            PlayModeStopReasonSessionStore.SetPending("cli-compile-stop-setting");

            PlayModeStopReasonSubscriber.HandleCompilationFinished(null);

            Assert.That(
                PlayModeStopReasonSessionStore.PendingReason,
                Is.EqualTo("cli-compile-stop-setting"));
        }

        /// <summary>
        /// What: compilationFinished with no pending does not invent a pending reason.
        /// </summary>
        [Test]
        public void HandleCompilationFinished_WhenNoPending_RemainsNoPending()
        {
            PlayModeStopReasonSubscriber.HandleCompilationFinished(null);

            Assert.That(PlayModeStopReasonSessionStore.PendingReason, Is.Null);
        }

        /// <summary>
        /// What: editor startup subscribed the production compilation and play-mode handlers.
        /// </summary>
        [Test]
        public void InitializeForEditorStartup_WhenEditorIsRunning_SubscribesProductionHandlers()
        {
            Assert.That(
                StaticEventHasHandler(
                    typeof(CompilationPipeline),
                    nameof(PlayModeStopReasonSubscriber.HandleCompilationStarted)),
                Is.True,
                "HandleCompilationStarted must be subscribed on CompilationPipeline.");
            Assert.That(
                StaticEventHasHandler(
                    typeof(CompilationPipeline),
                    nameof(PlayModeStopReasonSubscriber.HandleCompilationFinished)),
                Is.True,
                "HandleCompilationFinished must be subscribed on CompilationPipeline.");
            Assert.That(
                StaticEventHasHandler(
                    typeof(EditorApplication),
                    nameof(PlayModeStopReasonSubscriber.HandlePlayModeStateChanged)),
                Is.True,
                "HandlePlayModeStateChanged must be subscribed on EditorApplication.");
        }

        /// <summary>
        /// What: a non-exit play-mode event does not confirm pending.
        /// </summary>
        [Test]
        public void HandlePlayModeStateChanged_WhenNotExitingPlayMode_LeavesPending()
        {
            PlayModeStopReasonSessionStore.SetPending("cli-control-play-mode");

            PlayModeStopReasonSubscriber.HandlePlayModeStateChanged(PlayModeStateChange.EnteredPlayMode);

            Assert.That(PlayModeStopReasonSessionStore.PendingReason, Is.EqualTo("cli-control-play-mode"));
            Assert.That(PlayModeStopReasonSessionStore.TryReadConfirmed().HasValue, Is.False);
        }

        // Why: CompilationPipeline stores handlers on Delegate fields, but
        // EditorApplication.playModeStateChanged lives on EventWithPerformanceTracker
        // (m_PlayModeStateChangedEvent). A Delegate-only scan cannot see it.
        private static bool StaticEventHasHandler(Type eventOwner, string handlerName)
        {
            FieldInfo[] fields = eventOwner.GetFields(
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            for (int index = 0; index < fields.Length; index++)
            {
                object value = fields[index].GetValue(null);
                if (ContainsProductionHandler(value, handlerName))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsProductionHandler(object source, string handlerName)
        {
            if (source == null)
            {
                return false;
            }

            Delegate current = source as Delegate;
            if (current != null)
            {
                return InvocationListContains(current, handlerName);
            }

            return EnumeratorContainsHandler(source, handlerName);
        }

        private static bool InvocationListContains(Delegate current, string handlerName)
        {
            Delegate[] listeners = current.GetInvocationList();
            for (int listenerIndex = 0; listenerIndex < listeners.Length; listenerIndex++)
            {
                if (IsProductionHandler(listeners[listenerIndex], handlerName))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool EnumeratorContainsHandler(object source, string handlerName)
        {
            string typeName = source.GetType().Name;
            if (typeName.IndexOf("EventWithPerformanceTracker", StringComparison.Ordinal) < 0)
            {
                return false;
            }

            MethodInfo getEnumerator = source.GetType().GetMethod(
                "GetEnumerator",
                BindingFlags.Instance | BindingFlags.Public);
            if (getEnumerator == null || getEnumerator.GetParameters().Length != 0)
            {
                return false;
            }

            object enumerator = getEnumerator.Invoke(source, null);
            if (enumerator == null)
            {
                return false;
            }

            MethodInfo moveNext = enumerator.GetType().GetMethod("MoveNext");
            PropertyInfo currentProperty = enumerator.GetType().GetProperty("Current");
            if (moveNext == null || currentProperty == null)
            {
                return false;
            }

            while ((bool)moveNext.Invoke(enumerator, null))
            {
                Delegate listener = currentProperty.GetValue(enumerator) as Delegate;
                if (IsProductionHandler(listener, handlerName))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsProductionHandler(Delegate listener, string handlerName)
        {
            if (listener == null)
            {
                return false;
            }

            MethodInfo listenerMethod = listener.Method;
            return listenerMethod.DeclaringType == typeof(PlayModeStopReasonSubscriber)
                && listenerMethod.Name == handlerName;
        }
    }
}
