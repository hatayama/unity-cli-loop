using System;
using System.Diagnostics;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.Infrastructure;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies the get-editor-status payload built from the execution slot and the cached Editor state.
    /// </summary>
    [TestFixture]
    public sealed class EditorStatusBridgeCommandTests
    {
        [TearDown]
        public void TearDown()
        {
            UnityCliLoopEditorStateSnapshot.ClearForTesting();
        }

        /// <summary>
        /// What: an idle slot with recorded play and compile state reports not busy, no running tool,
        /// and each cached value in its own field.
        /// </summary>
        [Test]
        public void Execute_WhenSlotIsIdleAndStateIsRecorded_ReportsCachedState()
        {
            UnityCliLoopToolRegistrarService registrar = CreateRegistrar(new ToolExecutionSession());
            UnityCliLoopEditorStateSnapshot.SetPlayStateForTesting(isPlaying: true, isPaused: false);
            UnityCliLoopEditorStateSnapshot.SetCompileStateForTesting(isCompiling: false, isUpdating: true);

            GetEditorStatusResponse response = EditorStatusBridgeCommand.Execute(registrar, 1.5, Array.Empty<EditorDialogInfo>());

            Assert.That(response.IsBusy, Is.False);
            Assert.That(response.RunningToolName, Is.Null);
            Assert.That(response.RunningToolElapsedSeconds, Is.Null);
            Assert.That(response.RunningToolPhase, Is.Null);
            Assert.That(response.HasEditorState, Is.True);
            Assert.That(response.IsPlaying, Is.True);
            Assert.That(response.IsPaused, Is.False);
            Assert.That(response.IsCompiling, Is.False);
            Assert.That(response.IsUpdating, Is.True);
            Assert.That(response.SecondsSinceLastMainThreadTick, Is.EqualTo(1.5));
        }

        /// <summary>
        /// What: a held slot reports busy with the holder's name, elapsed seconds, and phase name.
        /// </summary>
        [Test]
        public void Execute_WhenToolHoldsSlot_ReportsRunningTool()
        {
            long timestamp = 0;
            ToolExecutionSession session = new ToolExecutionSession(() => timestamp);
            UnityCliLoopToolRegistrarService registrar = CreateRegistrar(session);
            ToolExecutionLease runningLease = session.TryEnter("run-tests").Lease;
            timestamp += 4 * Stopwatch.Frequency;

            GetEditorStatusResponse response = EditorStatusBridgeCommand.Execute(registrar, 0, Array.Empty<EditorDialogInfo>());

            Assert.That(response.IsBusy, Is.True);
            Assert.That(response.RunningToolName, Is.EqualTo("run-tests"));
            Assert.That(response.RunningToolElapsedSeconds, Is.EqualTo(4));
            Assert.That(response.RunningToolPhase, Is.EqualTo("Executing"));

            runningLease.Dispose();
        }

        /// <summary>
        /// What: a cache with nothing recorded reports no Editor state.
        /// </summary>
        [Test]
        public void Execute_WhenNothingIsCached_ReportsNoEditorState()
        {
            UnityCliLoopToolRegistrarService registrar = CreateRegistrar(new ToolExecutionSession());
            UnityCliLoopEditorStateSnapshot.ClearForTesting();

            GetEditorStatusResponse response = EditorStatusBridgeCommand.Execute(registrar, 0, Array.Empty<EditorDialogInfo>());

            Assert.That(response.HasEditorState, Is.False);
        }

        /// <summary>
        /// What: a cache with only the play state recorded reports no Editor state, because the
        /// compile values would be defaults rather than readings.
        /// </summary>
        [Test]
        public void Execute_WhenOnlyPlayStateIsCached_ReportsNoEditorState()
        {
            UnityCliLoopToolRegistrarService registrar = CreateRegistrar(new ToolExecutionSession());
            UnityCliLoopEditorStateSnapshot.ClearForTesting();
            UnityCliLoopEditorStateSnapshot.SetPlayStateForTesting(isPlaying: false, isPaused: false);

            GetEditorStatusResponse response = EditorStatusBridgeCommand.Execute(registrar, 0, Array.Empty<EditorDialogInfo>());

            Assert.That(response.HasEditorState, Is.False);
        }

        /// <summary>
        /// What: a cache with only the compile state recorded reports no Editor state, because the
        /// play values would be defaults rather than readings.
        /// </summary>
        [Test]
        public void Execute_WhenOnlyCompileStateIsCached_ReportsNoEditorState()
        {
            UnityCliLoopToolRegistrarService registrar = CreateRegistrar(new ToolExecutionSession());
            UnityCliLoopEditorStateSnapshot.ClearForTesting();
            UnityCliLoopEditorStateSnapshot.SetCompileStateForTesting(isCompiling: false, isUpdating: false);

            GetEditorStatusResponse response = EditorStatusBridgeCommand.Execute(registrar, 0, Array.Empty<EditorDialogInfo>());

            Assert.That(response.HasEditorState, Is.False);
        }

        /// <summary>
        /// What: a missing registrar is rejected instead of producing a payload that claims the slot is idle.
        /// </summary>
        [Test]
        public void Execute_WhenRegistrarIsNull_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => EditorStatusBridgeCommand.Execute(null, 0, Array.Empty<EditorDialogInfo>()));
        }

        /// <summary>
        /// What: the dialogs the tracker recorded are reported with their title, message, and buttons unchanged.
        /// </summary>
        [Test]
        public void Execute_WhenTrackerHasOpenDialog_ReportsIt()
        {
            UnityCliLoopToolRegistrarService registrar = CreateRegistrar(new ToolExecutionSession());
            EditorDialogTracker tracker = new EditorDialogTracker();
            tracker.RecordShown(1, "Save scene?", "Your changes will be lost.", new[] { "Save", "Cancel" });

            GetEditorStatusResponse response = EditorStatusBridgeCommand.Execute(registrar, 6, tracker.GetOpenDialogs());

            Assert.That(response.OpenDialogs, Has.Length.EqualTo(1));
            Assert.That(response.OpenDialogs[0].Title, Is.EqualTo("Save scene?"));
            Assert.That(response.OpenDialogs[0].Message, Is.EqualTo("Your changes will be lost."));
            Assert.That(response.OpenDialogs[0].Buttons, Is.EqualTo(new[] { "Save", "Cancel" }));
        }

        /// <summary>
        /// What: a missing dialog list is rejected instead of producing a payload that claims no dialog is open.
        /// </summary>
        [Test]
        public void Execute_WhenOpenDialogsIsNull_ThrowsArgumentNullException()
        {
            UnityCliLoopToolRegistrarService registrar = CreateRegistrar(new ToolExecutionSession());

            Assert.Throws<ArgumentNullException>(() => EditorStatusBridgeCommand.Execute(registrar, 0, null));
        }

        private static UnityCliLoopToolRegistrarService CreateRegistrar(ToolExecutionSession session)
        {
            return new UnityCliLoopToolRegistrarService(
                new EmptyInternalToolNameProvider(),
                new ToolSettingsRepository(),
                new UnityCliLoopToolExecutionService(new NoOpEditorRuntimeStatePort(), session),
                () => Array.Empty<IUnityCliLoopTool>());
        }
    }
}
