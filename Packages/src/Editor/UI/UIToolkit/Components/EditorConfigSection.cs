using System;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace io.github.hatayama.uLoopMCP
{
    /// <summary>
    /// UI section for LLM tool configuration (Cursor, Claude Code, etc).
    /// Handles editor type selection and MCP settings file generation.
    /// </summary>
    public class EditorConfigSection
    {
        private readonly EnumField _editorTypeField;
        private readonly VisualElement _repositoryRootRow;
        private readonly Toggle _repositoryRootToggle;
        private readonly Label _configErrorLabel;
        private readonly Button _configureButton;
        private readonly Button _deleteConfigButton;
        private readonly Button _openSettingsButton;

        private EditorConfigData _lastData;
        private bool _isInitialized;

        public event Action<McpEditorType> OnEditorTypeChanged;
        public event Action<bool> OnRepositoryRootChanged;
        public event Action OnConfigureClicked;
        public event Action OnDeleteConfigClicked;
        public event Action OnOpenSettingsClicked;

        public EditorConfigSection(VisualElement root)
        {
            _editorTypeField = root.Q<EnumField>("editor-type-field");
            _repositoryRootRow = root.Q<VisualElement>("repository-root-row");
            _repositoryRootToggle = root.Q<Toggle>("repository-root-toggle");
            _configErrorLabel = root.Q<Label>("config-error-label");
            _configureButton = root.Q<Button>("configure-button");
            _deleteConfigButton = root.Q<Button>("delete-config-button");
            _openSettingsButton = root.Q<Button>("open-settings-button");

            SetupBindings();
        }

        private void SetupBindings()
        {
            _repositoryRootToggle.RegisterValueChangedCallback(evt =>
            {
                // Foldout uses an internal Toggle that listens for ChangeEvent<bool>.
                // Without StopPropagation, this event bubbles up and collapses the Foldout.
                evt.StopPropagation();
                OnRepositoryRootChanged?.Invoke(evt.newValue);
            });
            _configureButton.clicked += () => OnConfigureClicked?.Invoke();
            _deleteConfigButton.clicked += () => OnDeleteConfigClicked?.Invoke();
            _openSettingsButton.clicked += () => OnOpenSettingsClicked?.Invoke();
        }

        public void Update(EditorConfigData data)
        {
            if (_lastData != null && _lastData.Equals(data))
            {
                return;
            }

            _lastData = data;

            InitializeEnumFieldIfNeeded(data);

            UpdateRepositoryRootRow(data);
            UpdateConfigError(data);
            UpdateConfigureButton(data);
            UpdateDeleteButton(data);
            UpdateOpenSettingsButton(data);
        }

        /// <summary>
        /// EnumField.Init() can only be called once; subsequent calls reset the field and cause visual glitches.
        /// </summary>
        private void InitializeEnumFieldIfNeeded(EditorConfigData data)
        {
            if (!_isInitialized)
            {
                _editorTypeField.Init(data.SelectedEditor);
                _editorTypeField.RegisterValueChangedCallback(evt =>
                {
                    if (evt.newValue is McpEditorType newValue)
                    {
                        OnEditorTypeChanged?.Invoke(newValue);
                    }
                });
                _isInitialized = true;
            }
            else
            {
                ViewDataBinder.UpdateEnumField(_editorTypeField, data.SelectedEditor);
            }
        }

        private void UpdateRepositoryRootRow(EditorConfigData data)
        {
            bool showRow = data.SupportsRepositoryRootToggle && data.ShowRepositoryRootToggle;
            ViewDataBinder.SetVisible(_repositoryRootRow, showRow);

            if (showRow)
            {
                ViewDataBinder.UpdateToggle(_repositoryRootToggle, data.AddRepositoryRoot);
            }
        }

        private void UpdateConfigError(EditorConfigData data)
        {
            bool hasError = !string.IsNullOrEmpty(data.ConfigurationError);
            ViewDataBinder.ToggleClass(_configErrorLabel, "mcp-error-message--visible", hasError);

            if (hasError)
            {
                string editorName = GetEditorDisplayName(data.SelectedEditor);
                _configErrorLabel.text = $"Error loading {editorName} configuration: {data.ConfigurationError}";
            }
        }

        private void UpdateConfigureButton(EditorConfigData data)
        {
            string editorName = GetEditorDisplayName(data.SelectedEditor);
            string buttonText;
            bool buttonEnabled = true;

            if (data.IsChecking)
            {
                buttonText = $"Checking {editorName} Settings...";
                buttonEnabled = false;
            }
            else if (data.IsConfigured)
            {
                if (data.IsUpdateNeeded)
                {
                    if (data.HasPortMismatch)
                    {
                        buttonText = data.IsServerRunning
                            ? $"Update {editorName} Settings\n(Port mismatch - Server: {data.CurrentPort})"
                            : $"Update {editorName} Settings\n(Port mismatch)";
                    }
                    else
                    {
                        buttonText = data.IsServerRunning
                            ? $"Update {editorName} Settings\n(Port {data.CurrentPort})"
                            : $"Update {editorName} Settings";
                    }
                }
                else
                {
                    buttonText = $"Settings Already Configured\n(Port {data.CurrentPort})";
                    buttonEnabled = false;
                }
            }
            else
            {
                buttonText = $"Settings not found. \nConfigure {editorName}";
            }

            _configureButton.text = buttonText;
            _configureButton.SetEnabled(buttonEnabled);

            UpdateButtonStyle(data, buttonEnabled);
        }

        private void UpdateButtonStyle(EditorConfigData data, bool buttonEnabled)
        {
            _configureButton.RemoveFromClassList("mcp-button--disabled");
            _configureButton.RemoveFromClassList("mcp-button--warning");

            if (!buttonEnabled)
            {
                _configureButton.AddToClassList("mcp-button--disabled");
            }
            else if (!data.IsConfigured || data.HasPortMismatch)
            {
                _configureButton.AddToClassList("mcp-button--warning");
            }
        }

        private void UpdateDeleteButton(EditorConfigData data)
        {
            ViewDataBinder.SetVisible(_deleteConfigButton, data.IsConfigured);

            if (data.IsConfigured)
            {
                string editorName = GetEditorDisplayName(data.SelectedEditor);
                _deleteConfigButton.text = $"Delete {editorName} Configuration";
            }
        }

        private void UpdateOpenSettingsButton(EditorConfigData data)
        {
            string editorName = GetEditorDisplayName(data.SelectedEditor);
            _openSettingsButton.text = $"Open {editorName} Settings File";
        }

        private string GetEditorDisplayName(McpEditorType editorType)
        {
            return editorType switch
            {
                McpEditorType.Cursor => "Cursor",
                McpEditorType.ClaudeCode => "Claude Code",
                McpEditorType.VSCode => "VSCode",
                McpEditorType.GeminiCLI => "Gemini CLI",
                McpEditorType.Codex => "Codex",
                McpEditorType.McpInspector => "MCP Inspector",
                _ => editorType.ToString()
            };
        }
    }
}
