using System;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.Infrastructure;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies when the settings file recovery scheduled at Editor startup runs.
    /// </summary>
    public sealed class UnityCliLoopEditorSettingsRecoverySchedulerTests
    {
        /// <summary>
        /// Verifies the startup recovery does not run inline but waits on the main-thread dispatcher, and runs
        /// once when the dispatcher drains its queue.
        /// </summary>
        [Test]
        public void ScheduleForEditorStartup_QueuesTheRecoveryOnTheDispatcher_AndRunsItOnce()
        {
            CountingEditorSettingsPort editorSettingsPort = new CountingEditorSettingsPort();
            QueueingDispatcher dispatcher = new QueueingDispatcher();
            MainThreadSwitcher.RegisterService(dispatcher);

            try
            {
                UnityCliLoopEditorSettingsRecoveryScheduler.ScheduleForEditorStartup(editorSettingsPort);

                Assert.That(editorSettingsPort.RecoverCount, Is.EqualTo(0));

                dispatcher.RunQueued();

                Assert.That(editorSettingsPort.RecoverCount, Is.EqualTo(1));
            }
            finally
            {
                EditorMainThreadDispatcherRestorer.Restore();
            }
        }

        private sealed class CountingEditorSettingsPort : IUnityCliLoopEditorSettingsPort
        {
            internal int RecoverCount { get; private set; }

            public void RecoverSettingsFileIfNeeded()
            {
                RecoverCount++;
            }

            public UnityCliLoopEditorSettingsData GetSettings()
            {
                throw new NotSupportedException();
            }

            public void SaveSettings(UnityCliLoopEditorSettingsData settings)
            {
                throw new NotSupportedException();
            }

            public void UpdateSettings(Func<UnityCliLoopEditorSettingsData, UnityCliLoopEditorSettingsData> transform)
            {
                throw new NotSupportedException();
            }

            public string GetLastSeenSetupWizardVersion()
            {
                throw new NotSupportedException();
            }

            public bool GetSuppressSetupWizardAutoShow()
            {
                throw new NotSupportedException();
            }

            public void SetSuppressSetupWizardAutoShow(bool suppressAutoShow)
            {
                throw new NotSupportedException();
            }

            public void SetShowToolSettings(bool showToolSettings)
            {
                throw new NotSupportedException();
            }

            public void SetInstallSkillsFlat(bool installSkillsFlat)
            {
                throw new NotSupportedException();
            }
        }
    }
}
