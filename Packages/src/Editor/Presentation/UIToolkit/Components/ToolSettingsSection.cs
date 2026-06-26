using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

using io.github.hatayama.UnityCliLoop.Application;

namespace io.github.hatayama.UnityCliLoop.Presentation
{
    /// <summary>
    /// UI section for the virtualized per-tool enable list.
    /// The expensive list is allowed to stay unloaded while the foldout is collapsed.
    /// </summary>
    public class ToolSettingsSection
    {
        private const int ToolListRowHeight = 24;
        private const int ToolDetailsRowHeight = 132;
        private const int InlineToolRowLimit = 40;
        private const string ToolSettingsInfoText = "Enable or disable tools. Disabled tools are hidden from AI agents.";

        private readonly Foldout _foldout;
        private readonly VisualElement _toolSettingsInfoContainer;
        private readonly VisualElement _toolListContainer;
        private readonly Label _toolListStatusLabel;
        private readonly ListView _toolListView;
        private readonly List<ToolListRowData> _toolListRows = new();
        private readonly Dictionary<string, Toggle> _togglesByToolName = new();
        private string _expandedDetailsToolName = string.Empty;
        private bool _isRegistryAvailable;
        private bool _isUnavailableStateShown;
        private bool _isLoadingStateShown;
        private string _layoutSignature = string.Empty;

        public event Action<bool> OnFoldoutChanged;
        public event Action<string, bool> OnToolToggled;

        public ToolSettingsSection(VisualElement root)
        {
            _foldout = root.Q<Foldout>("tool-settings-foldout");
            _toolSettingsInfoContainer = root.Q<VisualElement>("tool-settings-info-container");
            _toolListContainer = root.Q<VisualElement>("tool-list-container");
            Debug.Assert(_toolListContainer != null, "tool-list-container must not be null");

            SetupToolSettingsInfoText();
            _toolListStatusLabel = CreateToolListStatusLabel();
            _toolListView = CreateToolListView();
            _toolListContainer.Add(_toolListStatusLabel);
            _toolListContainer.Add(_toolListView);
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
            for (int i = 0; i < _toolListRows.Count; i++)
            {
                ToolListRowData row = _toolListRows[i];
                if (!row.IsTool || row.ToolName != toolName)
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

            HideToolDetails();
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

            HideToolDetails();
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

            HideToolDetails();
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

        private void SetupToolSettingsInfoText()
        {
            if (_toolSettingsInfoContainer == null)
            {
                return;
            }

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

        private void Rebuild(ToolSettingsSectionData data)
        {
            _toolListRows.Clear();
            _togglesByToolName.Clear();
            HideToolDetails();

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
                if (!row.IsTool || row.ToolName != toolName)
                {
                    continue;
                }

                row.IsEnabled = isEnabled;
                return;
            }
        }

        private static string CreateLayoutSignature(ToolSettingsSectionData data)
        {
            StringBuilder builder = new();
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
                builder.Append(item.SkillDescription);
                builder.Append('|');
            }

            builder.Append(';');
        }

        private static Label CreateToolListStatusLabel()
        {
            Label label = new();
            label.name = "tool-list-status-label";
            label.AddToClassList("unity-cli-loop-tool-registry-unavailable");
            return label;
        }

        private ListView CreateToolListView()
        {
            ListView listView = new();
            listView.name = "tool-list-view";
            listView.AddToClassList("unity-cli-loop-tool-list-view");
            listView.virtualizationMethod = CollectionVirtualizationMethod.DynamicHeight;
            listView.selectionType = SelectionType.None;
            listView.itemsSource = _toolListRows;
            listView.makeItem = CreateToolListRowElement;
            listView.bindItem = BindToolListRowElement;
            listView.unbindItem = UnbindToolListRowElement;
            return listView;
        }

        private static VisualElement CreateToolListRowElement()
        {
            VisualElement row = new();
            row.AddToClassList("unity-cli-loop-tool-toggle-row");
            row.AddToClassList("unity-cli-loop-tool-list-row");

            Toggle toggle = new();
            toggle.name = "tool-list-row-toggle";
            toggle.AddToClassList("unity-cli-loop-tool-toggle-row__toggle");
            toggle.RegisterValueChangedCallback(evt =>
            {
                evt.StopPropagation();

                if (row.userData is not ToolListRowData item || !item.IsTool)
                {
                    return;
                }

                item.Owner?.OnToolToggled?.Invoke(item.ToolName, evt.newValue);
            });

            Label label = new();
            label.name = "tool-list-row-label";
            label.AddToClassList("unity-cli-loop-tool-toggle-row__label");
            label.RegisterCallback<ClickEvent>(evt =>
            {
                evt.StopPropagation();

                if (row.userData is not ToolListRowData item || !item.IsTool)
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
            Button detailsButton = new();
            detailsButton.name = "tool-list-row-details-button";
            detailsButton.text = "Show Details";
            detailsButton.tooltip = "Show tool description";
            detailsButton.AddToClassList("unity-cli-loop-tool-toggle-row__details-button");
            detailsButton.RegisterCallback<ClickEvent>(evt =>
            {
                evt.StopPropagation();

                if (row.userData is not ToolListRowData item || !item.IsTool)
                {
                    return;
                }

                item.Owner?.ToggleToolDetailsForTool(item.ToolName);
            });
            row.Add(detailsButton);

            VisualElement detailsPanel = new();
            detailsPanel.name = "tool-list-row-details";
            detailsPanel.AddToClassList("unity-cli-loop-tool-details-panel");

            TextField detailsBody = new();
            detailsBody.name = "tool-list-row-details-body";
            detailsBody.isReadOnly = true;
            detailsBody.multiline = true;
            detailsBody.AddToClassList("unity-cli-loop-tool-details-panel__body");
            detailsPanel.Add(detailsBody);

            row.Add(detailsPanel);
            return row;
        }

        private void BindToolListRowElement(VisualElement row, int index)
        {
            Debug.Assert(index >= 0 && index < _toolListRows.Count, "tool list index must be valid");

            ToolListRowData item = _toolListRows[index];
            item.Owner = this;
            row.userData = item;

            Toggle toggle = row.Q<Toggle>("tool-list-row-toggle");
            Button detailsButton = row.Q<Button>("tool-list-row-details-button");
            Label label = row.Q<Label>("tool-list-row-label");
            VisualElement detailsPanel = row.Q<VisualElement>("tool-list-row-details");
            TextField detailsBody = row.Q<TextField>("tool-list-row-details-body");
            Debug.Assert(toggle != null, "tool-list-row-toggle must not be null");
            Debug.Assert(detailsButton != null, "tool-list-row-details-button must not be null");
            Debug.Assert(label != null, "tool-list-row-label must not be null");
            Debug.Assert(detailsPanel != null, "tool-list-row-details must not be null");
            Debug.Assert(detailsBody != null, "tool-list-row-details-body must not be null");

            ResetRowClasses(row, label, detailsButton);

            if (item.IsHeader)
            {
                BindHeaderRow(row, toggle, detailsButton, label, detailsPanel, item);
                return;
            }

            if (item.IsDetails)
            {
                BindDetailsRow(row, toggle, detailsButton, label, detailsPanel, detailsBody, item);
                return;
            }

            BindToolRow(row, toggle, detailsButton, label, detailsPanel, item);
        }

        private void BindHeaderRow(
            VisualElement row,
            Toggle toggle,
            Button detailsButton,
            Label label,
            VisualElement detailsPanel,
            ToolListRowData item)
        {
            ViewDataBinder.SetVisible(toggle, false);
            ViewDataBinder.SetVisible(detailsButton, false);
            ViewDataBinder.SetVisible(label, true);
            ViewDataBinder.SetVisible(detailsPanel, false);
            label.text = item.Label;
            label.tooltip = string.Empty;
            row.SetEnabled(true);
            row.AddToClassList("unity-cli-loop-tool-list-row--header");
            label.AddToClassList("unity-cli-loop-tool-group-header");
        }

        private void BindToolRow(
            VisualElement row,
            Toggle toggle,
            Button detailsButton,
            Label label,
            VisualElement detailsPanel,
            ToolListRowData item)
        {
            ViewDataBinder.SetVisible(toggle, true);
            ViewDataBinder.SetVisible(label, true);
            ViewDataBinder.SetVisible(detailsPanel, false);
            toggle.SetValueWithoutNotify(item.IsEnabled);
            bool hasDescription = !string.IsNullOrWhiteSpace(item.SkillDescription);
            bool isDetailsExpanded = hasDescription && _expandedDetailsToolName == item.ToolName;
            ViewDataBinder.SetVisible(detailsButton, hasDescription);
            detailsButton.text = isDetailsExpanded ? "Hide Details" : "Show Details";
            detailsButton.tooltip = isDetailsExpanded ? "Hide tool description" : "Show tool description";
            if (isDetailsExpanded)
            {
                detailsButton.AddToClassList("unity-cli-loop-tool-toggle-row__details-button--selected");
            }
            label.text = item.ToolName;
            label.tooltip = string.Empty;

            row.SetEnabled(true);
            _togglesByToolName[item.ToolName] = toggle;
        }

        private static void BindDetailsRow(
            VisualElement row,
            Toggle toggle,
            Button detailsButton,
            Label label,
            VisualElement detailsPanel,
            TextField detailsBody,
            ToolListRowData item)
        {
            ViewDataBinder.SetVisible(toggle, false);
            ViewDataBinder.SetVisible(detailsButton, false);
            ViewDataBinder.SetVisible(label, false);
            ViewDataBinder.SetVisible(detailsPanel, true);
            detailsBody.SetValueWithoutNotify(item.SkillDescription);
            row.SetEnabled(true);
            row.AddToClassList("unity-cli-loop-tool-details-row");
        }

        private static void ResetRowClasses(VisualElement row, Label label, Button detailsButton)
        {
            row.RemoveFromClassList("unity-cli-loop-tool-list-row--header");
            row.RemoveFromClassList("unity-cli-loop-tool-details-row");
            label.RemoveFromClassList("unity-cli-loop-tool-group-header");
            detailsButton.RemoveFromClassList("unity-cli-loop-tool-toggle-row__details-button--selected");
        }

        private void UnbindToolListRowElement(VisualElement row, int index)
        {
            if (row.userData is ToolListRowData item && item.IsTool)
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

        internal void ToggleToolDetailsForTool(string toolName)
        {
            Debug.Assert(!string.IsNullOrEmpty(toolName), "toolName must not be null or empty");

            ToolListRowData item = FindToolRowByName(toolName);
            Debug.Assert(item != null, "tool row must exist");
            Debug.Assert(!string.IsNullOrWhiteSpace(item.SkillDescription), "tool row must have details");

            if (_expandedDetailsToolName == item.ToolName)
            {
                HideToolDetails();
                UpdateToolListHeight();
                RefreshToolListView();
                return;
            }

            _expandedDetailsToolName = item.ToolName;
            RebuildToolDetailsRow();
            UpdateToolListHeight();
            RefreshToolListView();
        }

        private ToolListRowData FindToolRowByName(string toolName)
        {
            int toolIndex = FindToolRowIndex(toolName);
            if (toolIndex < 0)
            {
                return null;
            }

            return _toolListRows[toolIndex];
        }

        private int FindToolRowIndex(string toolName)
        {
            for (int i = 0; i < _toolListRows.Count; i++)
            {
                ToolListRowData item = _toolListRows[i];
                if (!item.IsTool || item.ToolName != toolName)
                {
                    continue;
                }

                return i;
            }

            return -1;
        }

        private void HideToolDetails()
        {
            _expandedDetailsToolName = string.Empty;
            RemoveToolDetailsRows();
        }

        private void RebuildToolDetailsRow()
        {
            RemoveToolDetailsRows();

            if (string.IsNullOrEmpty(_expandedDetailsToolName))
            {
                return;
            }

            int toolIndex = FindToolRowIndex(_expandedDetailsToolName);
            if (toolIndex < 0)
            {
                _expandedDetailsToolName = string.Empty;
                return;
            }

            ToolListRowData toolRow = _toolListRows[toolIndex];
            _toolListRows.Insert(toolIndex + 1, ToolListRowData.CreateDetails(toolRow));
        }

        private void RemoveToolDetailsRows()
        {
            for (int i = _toolListRows.Count - 1; i >= 0; i--)
            {
                if (!_toolListRows[i].IsDetails)
                {
                    continue;
                }

                _toolListRows.RemoveAt(i);
            }
        }

        private void UpdateToolListHeight()
        {
            int visibleRows = Math.Min(CountNonDetailsRows(), InlineToolRowLimit);
            if (visibleRows <= 0)
            {
                visibleRows = 1;
            }

            int detailsRowHeight = HasDetailsRow() ? ToolDetailsRowHeight : 0;
            _toolListView.style.height = (visibleRows * ToolListRowHeight) + detailsRowHeight + 2;
        }

        private int CountNonDetailsRows()
        {
            int count = 0;
            for (int i = 0; i < _toolListRows.Count; i++)
            {
                if (_toolListRows[i].IsDetails)
                {
                    continue;
                }

                count++;
            }

            return count;
        }

        private bool HasDetailsRow()
        {
            for (int i = 0; i < _toolListRows.Count; i++)
            {
                if (_toolListRows[i].IsDetails)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Holds serialized data for Tool List Row behavior.
        /// </summary>
        private sealed class ToolListRowData
        {
            public readonly bool IsHeader;
            public readonly bool IsDetails;
            public readonly string ToolName;
            public readonly string Label;
            public readonly string SkillDescription;
            public bool IsEnabled;
            public ToolSettingsSection Owner;
            public bool IsTool => !IsHeader && !IsDetails;

            private ToolListRowData(
                bool isHeader,
                bool isDetails,
                string toolName,
                string label,
                string skillDescription,
                bool isEnabled)
            {
                IsHeader = isHeader;
                IsDetails = isDetails;
                ToolName = toolName;
                Label = label;
                SkillDescription = skillDescription;
                IsEnabled = isEnabled;
            }

            public static ToolListRowData CreateHeader(string label)
            {
                return new ToolListRowData(
                    true,
                    false,
                    string.Empty,
                    label,
                    string.Empty,
                    true);
            }

            public static ToolListRowData CreateTool(ToolToggleItem item)
            {
                return new ToolListRowData(
                    false,
                    false,
                    item.ToolName,
                    item.ToolName,
                    item.SkillDescription,
                    item.IsEnabled);
            }

            public static ToolListRowData CreateDetails(ToolListRowData toolRow)
            {
                Debug.Assert(toolRow != null, "toolRow must not be null");
                Debug.Assert(toolRow.IsTool, "toolRow must be a tool row");

                return new ToolListRowData(
                    false,
                    true,
                    toolRow.ToolName,
                    string.Empty,
                    toolRow.SkillDescription,
                    true);
            }
        }
    }
}
