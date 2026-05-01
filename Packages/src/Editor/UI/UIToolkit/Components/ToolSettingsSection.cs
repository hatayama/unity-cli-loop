using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

namespace io.github.hatayama.uLoopMCP
{
    /// <summary>
    /// UI section for tool permissions and a virtualized per-tool enable list.
    /// The expensive list is allowed to stay unloaded while the foldout is collapsed.
    /// </summary>
    public class ToolSettingsSection
    {
        private const int ToolListRowHeight = 24;
        private const int InlineToolRowLimit = 40;

        private readonly Foldout _foldout;
        private readonly Button _securityLevelRestrictedButton;
        private readonly Button _securityLevelFullAccessButton;
        private readonly Label _securityLevelDescription;
        private readonly VisualElement _toolSettingsInfoContainer;
        private readonly VisualElement _toolListContainer;
        private readonly Label _toolListStatusLabel;
        private readonly ListView _toolListView;
        private readonly List<ToolListRowData> _toolListRows = new();
        private readonly Dictionary<string, Toggle> _togglesByToolName = new();
        private bool _isRegistryAvailable;
        private bool _isUnavailableStateShown;
        private bool _isLoadingStateShown;
        private string _layoutSignature = string.Empty;

        public event Action<bool> OnFoldoutChanged;
        public event Action<string, bool> OnToolToggled;
        public event Action<DynamicCodeSecurityLevel> OnSecurityLevelChanged;

        public ToolSettingsSection(VisualElement root)
        {
            _foldout = root.Q<Foldout>("tool-settings-foldout");
            _securityLevelRestrictedButton = root.Q<Button>("security-level-restricted-button");
            _securityLevelFullAccessButton = root.Q<Button>("security-level-full-access-button");
            _securityLevelDescription = root.Q<Label>("security-level-description");
            _toolSettingsInfoContainer = root.Q<VisualElement>("tool-settings-info-container");
            _toolListContainer = root.Q<VisualElement>("tool-list-container");
            Debug.Assert(_toolListContainer != null, "tool-list-container must not be null");

            _toolListStatusLabel = CreateToolListStatusLabel();
            _toolListView = CreateToolListView();
            _toolListContainer.Add(_toolListStatusLabel);
            _toolListContainer.Add(_toolListView);
            ClearToolList();

            Label cliReferenceLink = root.Q<Label>("cli-reference-link");
            if (cliReferenceLink != null)
            {
                cliReferenceLink.RegisterCallback<ClickEvent>(_ => Application.OpenURL(McpUIConstants.CLI_COMMAND_REFERENCE_URL));
            }

            SetupBindings();
        }

        private void SetupBindings()
        {
            _foldout.RegisterValueChangedCallback(evt => OnFoldoutChanged?.Invoke(evt.newValue));

            _securityLevelRestrictedButton.clicked += () => UpdateSecurityLevel(DynamicCodeSecurityLevel.Restricted);
            _securityLevelFullAccessButton.clicked += () => UpdateSecurityLevel(DynamicCodeSecurityLevel.FullAccess);
        }

        public void Update(ToolSettingsSectionData data)
        {
            ViewDataBinder.UpdateFoldout(_foldout, data.ShowToolSettings);
            UpdateSecurityLevelSelection(data.DynamicCodeSecurityLevel);
            UpdateSecurityLevelDescription(data.DynamicCodeSecurityLevel);

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
            for (int i = 0; i < _toolListRows.Count; i++)
            {
                ToolListRowData row = _toolListRows[i];
                if (row.IsHeader || row.ToolName != toolName)
                {
                    continue;
                }

                row.IsEnabled = enabled;
                break;
            }

            if (_togglesByToolName.TryGetValue(toolName, out Toggle toggle))
            {
                toggle.SetValueWithoutNotify(enabled);
            }

            RefreshToolListView();
        }

        private void UpdateDeferredState()
        {
            if (_toolListRows.Count > 0 || _isUnavailableStateShown)
            {
                RefreshToolListView();
                return;
            }

            UpdateLoadingState();
        }

        private void UpdateLoadingState()
        {
            _toolListRows.Clear();
            _togglesByToolName.Clear();
            _layoutSignature = string.Empty;

            SetToolListStatus("Loading tools...");
            ViewDataBinder.SetVisible(_toolListView, false);
            SetToolSettingsInfoVisible(false);

            _isRegistryAvailable = false;
            _isUnavailableStateShown = false;
            _isLoadingStateShown = true;
        }

        private void UpdateUnavailableState()
        {
            _toolListRows.Clear();
            _togglesByToolName.Clear();
            _layoutSignature = string.Empty;

            SetToolListStatus("Tool registry not yet initialized. Start the server first.");
            ViewDataBinder.SetVisible(_toolListView, false);
            SetToolSettingsInfoVisible(false);

            _isRegistryAvailable = false;
            _isUnavailableStateShown = true;
            _isLoadingStateShown = false;
        }

        private void ClearToolList()
        {
            _toolListRows.Clear();
            _togglesByToolName.Clear();
            _layoutSignature = string.Empty;

            ViewDataBinder.SetVisible(_toolListStatusLabel, false);
            ViewDataBinder.SetVisible(_toolListView, false);
            SetToolSettingsInfoVisible(false);
            RefreshToolListView();

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
            if (_toolSettingsInfoContainer == null)
            {
                return;
            }

            ViewDataBinder.SetVisible(_toolSettingsInfoContainer, visible);
        }

        private void UpdateToolList(ToolSettingsSectionData data)
        {
            string layoutSignature = CreateLayoutSignature(data);
            bool shouldRebuild = !_isRegistryAvailable
                || _isUnavailableStateShown
                || _isLoadingStateShown
                || _layoutSignature != layoutSignature;

            if (shouldRebuild)
            {
                Rebuild(data);
                _layoutSignature = layoutSignature;
            }
            else
            {
                UpdateToggleStates(data.BuiltInTools);
                UpdateToggleStates(data.ThirdPartyTools);
            }

            ViewDataBinder.SetVisible(_toolListStatusLabel, false);
            ViewDataBinder.SetVisible(_toolListView, true);
            SetToolSettingsInfoVisible(true);
            RefreshToolListView();

            _isRegistryAvailable = true;
            _isUnavailableStateShown = false;
            _isLoadingStateShown = false;
        }

        private void UpdateSecurityLevelDescription(DynamicCodeSecurityLevel currentLevel)
        {
            string description = currentLevel switch
            {
                DynamicCodeSecurityLevel.Restricted => "Dangerous APIs blocked (recommended)",
                DynamicCodeSecurityLevel.FullAccess => "All APIs available (use with caution)",
                _ => "Unknown level"
            };

            _securityLevelDescription.text = description;
            _securityLevelDescription.RemoveFromClassList("mcp-security-level-description--warning");

            if (currentLevel == DynamicCodeSecurityLevel.FullAccess)
            {
                _securityLevelDescription.AddToClassList("mcp-security-level-description--warning");
            }
        }

        private void UpdateSecurityLevel(DynamicCodeSecurityLevel newLevel)
        {
            UpdateSecurityLevelSelection(newLevel);
            UpdateSecurityLevelDescription(newLevel);
            OnSecurityLevelChanged?.Invoke(newLevel);
        }

        private void UpdateSecurityLevelSelection(DynamicCodeSecurityLevel currentLevel)
        {
            bool isRestricted = currentLevel == DynamicCodeSecurityLevel.Restricted;
            bool isFullAccess = currentLevel == DynamicCodeSecurityLevel.FullAccess;

            ViewDataBinder.ToggleClass(_securityLevelRestrictedButton, "mcp-segmented-control__button--active", isRestricted);
            ViewDataBinder.ToggleClass(_securityLevelRestrictedButton, "mcp-segmented-control__button--warning-active", false);
            ViewDataBinder.ToggleClass(_securityLevelFullAccessButton, "mcp-segmented-control__button--active", isFullAccess);
            ViewDataBinder.ToggleClass(_securityLevelFullAccessButton, "mcp-segmented-control__button--warning-active", isFullAccess);
        }

        private void Rebuild(ToolSettingsSectionData data)
        {
            _toolListRows.Clear();
            _togglesByToolName.Clear();

            if (data.BuiltInTools.Length > 0)
            {
                _toolListRows.Add(ToolListRowData.CreateHeader("Built-in Tools"));
                AddToolRows(data.BuiltInTools);
            }

            if (data.ThirdPartyTools.Length > 0)
            {
                _toolListRows.Add(ToolListRowData.CreateHeader("Third Party Tools"));
                AddToolRows(data.ThirdPartyTools);
            }

            UpdateToolListHeight();
            RefreshToolListView();
        }

        private void AddToolRows(IReadOnlyList<ToolToggleItem> items)
        {
            for (int i = 0; i < items.Count; i++)
            {
                ToolToggleItem item = items[i];
                _toolListRows.Add(ToolListRowData.CreateTool(item));
            }
        }

        private void UpdateToggleStates(IReadOnlyList<ToolToggleItem> items)
        {
            for (int i = 0; i < items.Count; i++)
            {
                ToolToggleItem item = items[i];
                UpdateToggleState(item.ToolName, item.IsEnabled);
            }
        }

        private void UpdateToggleState(string toolName, bool isEnabled)
        {
            for (int i = 0; i < _toolListRows.Count; i++)
            {
                ToolListRowData row = _toolListRows[i];
                if (row.IsHeader || row.ToolName != toolName)
                {
                    continue;
                }

                row.IsEnabled = isEnabled;
                return;
            }
        }

        private static string CreateLayoutSignature(ToolSettingsSectionData data)
        {
            StringBuilder builder = new StringBuilder();
            AppendGroupSignature(builder, data.BuiltInTools, "B");
            AppendGroupSignature(builder, data.ThirdPartyTools, "T");
            return builder.ToString();
        }

        private static void AppendGroupSignature(StringBuilder builder, IReadOnlyList<ToolToggleItem> items, string group)
        {
            builder.Append(group);
            builder.Append(':');

            for (int i = 0; i < items.Count; i++)
            {
                ToolToggleItem item = items[i];
                builder.Append(item.ToolName);
                builder.Append('|');
            }

            builder.Append(';');
        }

        private static Label CreateToolListStatusLabel()
        {
            Label label = new Label();
            label.name = "tool-list-status-label";
            label.AddToClassList("mcp-tool-registry-unavailable");
            return label;
        }

        private ListView CreateToolListView()
        {
            ListView listView = new ListView();
            listView.name = "tool-list-view";
            listView.AddToClassList("mcp-tool-list-view");
            listView.fixedItemHeight = ToolListRowHeight;
            listView.virtualizationMethod = CollectionVirtualizationMethod.FixedHeight;
            listView.selectionType = SelectionType.None;
            listView.itemsSource = _toolListRows;
            listView.makeItem = CreateToolListRowElement;
            listView.bindItem = BindToolListRowElement;
            listView.unbindItem = UnbindToolListRowElement;
            return listView;
        }

        private static VisualElement CreateToolListRowElement()
        {
            VisualElement row = new VisualElement();
            row.AddToClassList("mcp-tool-toggle-row");
            row.AddToClassList("mcp-tool-list-row");

            Toggle toggle = new Toggle();
            toggle.name = "tool-list-row-toggle";
            toggle.AddToClassList("mcp-tool-toggle-row__toggle");
            toggle.RegisterValueChangedCallback(evt =>
            {
                evt.StopPropagation();

                if (row.userData is not ToolListRowData item || item.IsHeader)
                {
                    return;
                }

                item.Owner?.OnToolToggled?.Invoke(item.ToolName, evt.newValue);
            });

            Label label = new Label();
            label.name = "tool-list-row-label";
            label.AddToClassList("mcp-tool-toggle-row__label");
            label.RegisterCallback<ClickEvent>(evt =>
            {
                evt.StopPropagation();

                if (row.userData is not ToolListRowData item || item.IsHeader)
                {
                    return;
                }

                Toggle rowToggle = row.Q<Toggle>("tool-list-row-toggle");
                bool newValue = !rowToggle.value;
                rowToggle.SetValueWithoutNotify(newValue);
                item.Owner?.OnToolToggled?.Invoke(item.ToolName, newValue);
            });

            row.Add(toggle);
            row.Add(label);
            return row;
        }

        private void BindToolListRowElement(VisualElement row, int index)
        {
            Debug.Assert(index >= 0 && index < _toolListRows.Count, "tool list index must be valid");

            ToolListRowData item = _toolListRows[index];
            item.Owner = this;
            row.userData = item;

            Toggle toggle = row.Q<Toggle>("tool-list-row-toggle");
            Label label = row.Q<Label>("tool-list-row-label");
            Debug.Assert(toggle != null, "tool-list-row-toggle must not be null");
            Debug.Assert(label != null, "tool-list-row-label must not be null");

            ResetRowClasses(row, label);

            if (item.IsHeader)
            {
                BindHeaderRow(row, toggle, label, item);
                return;
            }

            BindToolRow(row, toggle, label, item);
        }

        private void BindHeaderRow(VisualElement row, Toggle toggle, Label label, ToolListRowData item)
        {
            ViewDataBinder.SetVisible(toggle, false);
            label.text = item.Label;
            label.tooltip = string.Empty;
            row.SetEnabled(true);
            row.AddToClassList("mcp-tool-list-row--header");
            label.AddToClassList("mcp-tool-group-header");
        }

        private void BindToolRow(VisualElement row, Toggle toggle, Label label, ToolListRowData item)
        {
            ViewDataBinder.SetVisible(toggle, true);
            toggle.SetValueWithoutNotify(item.IsEnabled);
            label.text = item.ToolName;
            label.tooltip = item.Description;

            row.SetEnabled(true);
            _togglesByToolName[item.ToolName] = toggle;
        }

        private static void ResetRowClasses(VisualElement row, Label label)
        {
            row.RemoveFromClassList("mcp-tool-list-row--header");
            label.RemoveFromClassList("mcp-tool-group-header");
        }

        private void UnbindToolListRowElement(VisualElement row, int index)
        {
            if (row.userData is ToolListRowData item && !item.IsHeader)
            {
                _togglesByToolName.Remove(item.ToolName);
            }

            row.userData = null;
        }

        private void RefreshToolListView()
        {
            _togglesByToolName.Clear();
            _toolListView.RefreshItems();
        }

        private void UpdateToolListHeight()
        {
            int visibleRows = Math.Min(_toolListRows.Count, InlineToolRowLimit);
            if (visibleRows <= 0)
            {
                visibleRows = 1;
            }

            _toolListView.style.height = (visibleRows * ToolListRowHeight) + 2;
        }

        private sealed class ToolListRowData
        {
            public readonly bool IsHeader;
            public readonly string ToolName;
            public readonly string Label;
            public readonly string Description;
            public bool IsEnabled;
            public ToolSettingsSection Owner;

            private ToolListRowData(
                bool isHeader,
                string toolName,
                string label,
                string description,
                bool isEnabled)
            {
                IsHeader = isHeader;
                ToolName = toolName;
                Label = label;
                Description = description;
                IsEnabled = isEnabled;
            }

            public static ToolListRowData CreateHeader(string label)
            {
                return new ToolListRowData(
                    true,
                    string.Empty,
                    label,
                    string.Empty,
                    true);
            }

            public static ToolListRowData CreateTool(ToolToggleItem item)
            {
                return new ToolListRowData(
                    false,
                    item.ToolName,
                    item.ToolName,
                    item.Description,
                    item.IsEnabled);
            }
        }
    }
}
