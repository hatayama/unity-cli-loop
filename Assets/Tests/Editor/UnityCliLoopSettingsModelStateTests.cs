using System;
using System.Collections.Generic;

using NUnit.Framework;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.Presentation;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies the Settings window model's state transitions and which of them persist through the
    /// settings ports.
    /// </summary>
    public sealed class UnityCliLoopSettingsModelStateTests
    {
        private RecordingEditorSettingsPort _editorSettingsPort;
        private RecordingToolSettingsPort _toolSettingsPort;
        private UnityCliLoopToolRegistrarService _registrarService;
        private ToolSettingsUseCase _toolSettingsUseCase;
        private UnityCliLoopSettingsModel _model;

        [SetUp]
        public void SetUp()
        {
            _editorSettingsPort = new RecordingEditorSettingsPort();
            _toolSettingsPort = new RecordingToolSettingsPort();
            _registrarService = new UnityCliLoopToolRegistrarService(
                new EmptyInternalToolNameProvider(),
                _toolSettingsPort,
                new UnityCliLoopToolExecutionService(new NoOpEditorRuntimeStatePort()),
                () => Array.Empty<IUnityCliLoopTool>());
            _toolSettingsUseCase = new ToolSettingsUseCase(
                _toolSettingsPort,
                _registrarService,
                new EmptySkillDescriptionProvider());
            _model = new UnityCliLoopSettingsModel(_toolSettingsUseCase, _editorSettingsPort);
        }

        /// <summary>
        /// Verifies loading takes the security and tool-settings flags from the stored settings while keeping the
        /// in-memory scroll position and configuration state.
        /// </summary>
        [Test]
        public void LoadFromSettings_CopiesThePersistedSectionFlagsAndKeepsTheRest()
        {
            Vector2 scrollPosition = new Vector2(0f, 42f);
            _model.UpdateUIState(ui => new UIState(scrollPosition, true, true, showConfiguration: false));
            _editorSettingsPort.Settings = new UnityCliLoopEditorSettingsData
            {
                showUnityCliLoopSecuritySetting = false,
                showToolSettings = false
            };

            _model.LoadFromSettings();

            Assert.That(_model.UI.ShowUnityCliLoopSecuritySetting, Is.False);
            Assert.That(_model.UI.ShowToolSettings, Is.False);
            Assert.That(_model.UI.MainScrollPosition, Is.EqualTo(scrollPosition));
            Assert.That(_model.UI.ShowConfiguration, Is.False);
        }

        /// <summary>
        /// Verifies toggling the Tool Settings section updates the UI state and stores the choice.
        /// </summary>
        [Test]
        public void UpdateShowToolSettings_UpdatesTheStateAndPersistsIt()
        {
            _model.UpdateShowToolSettings(false);

            Assert.That(_model.UI.ShowToolSettings, Is.False);
            Assert.That(_editorSettingsPort.ShowToolSettingsValues, Is.EqualTo(new List<bool> { false }));
        }

        /// <summary>
        /// Verifies the configuration foldout state changes in memory and is never persisted.
        /// </summary>
        [Test]
        public void UpdateShowConfiguration_UpdatesOnlyTheInMemoryState()
        {
            _model.UpdateShowConfiguration(false);

            Assert.That(_model.UI.ShowConfiguration, Is.False);
            Assert.That(_model.UI.ShowToolSettings, Is.True);
            Assert.That(_editorSettingsPort.ShowToolSettingsValues, Is.Empty);
        }

        /// <summary>
        /// Verifies enabling or disabling a tool writes the tool setting and notifies tool-change listeners.
        /// </summary>
        [Test]
        public void UpdateToolEnabled_StoresTheSettingAndAnnouncesTheChange()
        {
            int toolsChangedCount = 0;
            _toolSettingsUseCase.AddToolsChangedHandler(() => toolsChangedCount++);

            _model.UpdateToolEnabled("sample-tool", false);

            Assert.That(_toolSettingsPort.SetCalls, Is.EqualTo(new List<string> { "sample-tool=False" }));
            Assert.That(toolsChangedCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies entering post-compile mode also asks for a repaint without forgetting the last server state.
        /// </summary>
        [Test]
        public void EnablePostCompileMode_RequestsARepaintAndKeepsTheServerFlag()
        {
            _model.UpdateRuntimeState(runtime => new RuntimeState(lastServerRunning: true));

            _model.EnablePostCompileMode();

            Assert.That(_model.Runtime.IsPostCompileMode, Is.True);
            Assert.That(_model.Runtime.NeedsRepaint, Is.True);
            Assert.That(_model.Runtime.LastServerRunning, Is.True);
        }

        /// <summary>
        /// Verifies a repaint request can be cleared again while post-compile mode stays on.
        /// </summary>
        [Test]
        public void ClearRepaintRequest_AfterARequest_KeepsThePostCompileMode()
        {
            _model.EnablePostCompileMode();

            _model.ClearRepaintRequest();
            bool needsRepaintAfterClear = _model.Runtime.NeedsRepaint;
            _model.RequestRepaint();

            Assert.That(needsRepaintAfterClear, Is.False);
            Assert.That(_model.Runtime.NeedsRepaint, Is.True);
            Assert.That(_model.Runtime.IsPostCompileMode, Is.True);
        }

        private sealed class RecordingEditorSettingsPort : IUnityCliLoopEditorSettingsPort
        {
            internal UnityCliLoopEditorSettingsData Settings { get; set; } = new UnityCliLoopEditorSettingsData();
            internal List<bool> ShowToolSettingsValues { get; } = new List<bool>();

            public UnityCliLoopEditorSettingsData GetSettings()
            {
                return Settings;
            }

            public void SetShowToolSettings(bool showToolSettings)
            {
                ShowToolSettingsValues.Add(showToolSettings);
            }

            public void RecoverSettingsFileIfNeeded() => throw new NotSupportedException();
            public void SaveSettings(UnityCliLoopEditorSettingsData settings) => throw new NotSupportedException();

            public void UpdateSettings(Func<UnityCliLoopEditorSettingsData, UnityCliLoopEditorSettingsData> transform) =>
                throw new NotSupportedException();

            public string GetLastSeenSetupWizardVersion() => throw new NotSupportedException();
            public bool GetSuppressSetupWizardAutoShow() => throw new NotSupportedException();
            public void SetSuppressSetupWizardAutoShow(bool suppressAutoShow) => throw new NotSupportedException();
            public void SetInstallSkillsFlat(bool installSkillsFlat) => throw new NotSupportedException();
        }

        private sealed class RecordingToolSettingsPort : IToolSettingsPort
        {
            internal List<string> SetCalls { get; } = new List<string>();

            public void SetToolEnabled(string toolName, bool enabled)
            {
                SetCalls.Add($"{toolName}={enabled}");
            }

            public bool IsToolEnabled(string toolName) => throw new NotSupportedException();
            public string[] GetDisabledTools() => throw new NotSupportedException();
            public void InvalidateCache() => throw new NotSupportedException();
        }

        private sealed class EmptySkillDescriptionProvider : IToolSkillDescriptionProvider
        {
            public IReadOnlyDictionary<string, string> GetSkillDescriptionsByToolName()
            {
                return new Dictionary<string, string>();
            }
        }
    }
}
