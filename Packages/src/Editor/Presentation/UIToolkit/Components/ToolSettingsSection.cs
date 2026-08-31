using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace io.github.hatayama.UnityCliLoop.Presentation
{
    /// <summary>
    /// UI section for the virtualized per-tool enable list.
    /// The expensive list is allowed to stay unloaded while the foldout is collapsed.
    /// </summary>
    public class ToolSettingsSection
    {
        private const string ToolSettingsInfoText = "Enable or disable tools. Disabled tools are hidden from AI agents.";

        private readonly Foldout _foldout;
        private readonly VisualElement _toolSettingsInfoContainer;
        private readonly VisualElement _toolListContainer;
        private readonly Label _toolListStatusLabel;
        private readonly ToolSettingsSectionListViewController _listViewController;
        private bool _isRegistryAvailable;
        private bool _isUnavailableStateShown;
        private bool _isLoadingStateShown;

        public event Action<bool> OnFoldoutChanged;
        public event Action<string, bool> OnToolToggled;

        public ToolSettingsSection(VisualElement root)
        {
            _foldout = root.Q<Foldout>("tool-settings-foldout");
            _toolSettingsInfoContainer = root.Q<VisualElement>("tool-settings-info-container");
            _toolListContainer = root.Q<VisualElement>("tool-list-container");
            Debug.Assert(_toolSettingsInfoContainer != null, "tool-settings-info-container must not be null");
            Debug.Assert(_toolListContainer != null, "tool-list-container must not be null");

            SetupToolSettingsInfoText();
            _toolListStatusLabel = CreateToolListStatusLabel();
            _listViewController = new ToolSettingsSectionListViewController(
                this,
                (toolName, enabled) => OnToolToggled?.Invoke(toolName, enabled));
            _toolListContainer.Add(_toolListStatusLabel);
            _toolListContainer.Add(_listViewController.ListView);
            ClearToolList();

            SetupBindings();
        }

        private void SetupBindings()
        {
            _foldout.RegisterValueChangedCallback(evt => OnFoldoutChanged?.Invoke(evt.newValue));
        }

        public void Update(ToolSettingsSectionData data)
        {
            ViewDataBinder.UpdateFoldout(_foldout, data.ShowToolSettings);

            if (!data.ShowToolSettings)
            {
                ClearToolList();
                return;
            }

            if (!data.HasToolListData)
            {
                UpdateDeferredState();
                return;
            }

            if (!data.IsRegistryAvailable)
            {
                UpdateUnavailableState();
                return;
            }

            UpdateToolList(data);
        }

        public void UpdateSingleToggle(string toolName, bool enabled)
        {
            _listViewController.UpdateSingleToggle(toolName, enabled);
        }

        private void UpdateDeferredState()
        {
            if (_listViewController.HasRows || _isUnavailableStateShown)
            {
                _listViewController.RefreshToolListView();
                return;
            }

            UpdateLoadingState();
        }

        private void UpdateLoadingState()
        {
            _listViewController.ResetRows();
            SetToolListStatus("Loading tools...");
            _listViewController.SetListViewVisible(false);
            SetToolSettingsInfoVisible(false);

            _isRegistryAvailable = false;
            _isUnavailableStateShown = false;
            _isLoadingStateShown = true;
        }

        private void UpdateUnavailableState()
        {
            _listViewController.ResetRows();
            SetToolListStatus("Tool registry not yet initialized. Start the server first.");
            _listViewController.SetListViewVisible(false);
            SetToolSettingsInfoVisible(false);

            _isRegistryAvailable = false;
            _isUnavailableStateShown = true;
            _isLoadingStateShown = false;
        }

        private void ClearToolList()
        {
            _listViewController.Clear();
            ViewDataBinder.SetVisible(_toolListStatusLabel, false);
            SetToolSettingsInfoVisible(false);

            _isRegistryAvailable = false;
            _isUnavailableStateShown = false;
            _isLoadingStateShown = false;
        }

        private void SetToolListStatus(string text)
        {
            _toolListStatusLabel.text = text;
            ViewDataBinder.SetVisible(_toolListStatusLabel, true);
        }

        private void SetToolSettingsInfoVisible(bool visible)
        {
            ViewDataBinder.SetVisible(_toolSettingsInfoContainer, visible);
        }

        private void SetupToolSettingsInfoText()
        {
            _toolSettingsInfoContainer.Clear();

            TextField infoText = new();
            infoText.name = "tool-settings-info-text";
            infoText.isReadOnly = true;
            infoText.multiline = true;
            infoText.SetValueWithoutNotify(ToolSettingsInfoText);
            infoText.AddToClassList("unity-cli-loop-tool-settings-info");

            _toolSettingsInfoContainer.Add(infoText);
        }

        private void UpdateToolList(ToolSettingsSectionData data)
        {
            bool forceRebuild = !_isRegistryAvailable
                || _isUnavailableStateShown
                || _isLoadingStateShown;

            _listViewController.UpdateToolList(data, forceRebuild);

            ViewDataBinder.SetVisible(_toolListStatusLabel, false);
            SetToolSettingsInfoVisible(true);

            _isRegistryAvailable = true;
            _isUnavailableStateShown = false;
            _isLoadingStateShown = false;
        }

        private static Label CreateToolListStatusLabel()
        {
            Label label = new();
            label.name = "tool-list-status-label";
            label.AddToClassList("unity-cli-loop-tool-registry-unavailable");
            return label;
        }

        internal void ToggleToolDetailsForTool(string toolName)
        {
            _listViewController.ToggleToolDetailsForTool(toolName);
        }
    }
}
