using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace io.github.hatayama.UnityCliLoop.Presentation
{
    /// <summary>
    /// Owns the virtualized tool ListView and its row model for ToolSettingsSection.
    /// </summary>
    internal sealed class ToolSettingsSectionListViewController
    {
        private const int ToolListRowHeight = 24;
        private const int ToolDetailsRowHeight = 132;
        private const int InlineToolRowLimit = 40;

        private readonly ListView _toolListView;
        private readonly List<ToolListRowData> _toolListRows = new();
        private readonly Dictionary<string, Toggle> _togglesByToolName = new();
        private string _expandedDetailsToolName = string.Empty;
        private string _layoutSignature = string.Empty;
        private readonly ToolSettingsSectionRowBinder _rowBinder;

        public ListView ListView => _toolListView;
        public bool HasRows => _toolListRows.Count > 0;
        public ToolSettingsSectionListViewController(
            ToolSettingsSection owner,
            Action<string, bool> onToolToggled)
        {
            Debug.Assert(owner != null, "owner must not be null");
            Debug.Assert(onToolToggled != null, "onToolToggled must not be null");
            _rowBinder = new ToolSettingsSectionRowBinder(
                owner,
                onToolToggled,
                _toolListRows,
                _togglesByToolName,
                () => _expandedDetailsToolName);
            _toolListView = CreateToolListView();
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

        public void Clear()
        {
            ResetRows();
            ViewDataBinder.SetVisible(_toolListView, false);
            RefreshToolListView();
        }
        public void ResetRows()
        {
            _toolListRows.Clear();
            _togglesByToolName.Clear();
            _layoutSignature = string.Empty;
            HideToolDetails();
        }

        public void SetListViewVisible(bool visible) => ViewDataBinder.SetVisible(_toolListView, visible);

        public void UpdateToolList(ToolSettingsSectionData data, bool forceRebuild)
        {
            string layoutSignature = ToolSettingsSectionLayoutSignature.Create(data);
            bool shouldRebuild = forceRebuild || _layoutSignature != layoutSignature;
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

            ViewDataBinder.SetVisible(_toolListView, true);
            RefreshToolListView();
        }

        public void ToggleToolDetailsForTool(string toolName)
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

        public void RefreshToolListView()
        {
            _togglesByToolName.Clear();
            _toolListView.RefreshItems();
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
                _toolListRows.Add(ToolListRowData.CreateTool(items[i]));
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

        private ListView CreateToolListView()
        {
            ListView listView = new();
            listView.name = "tool-list-view";
            listView.AddToClassList("unity-cli-loop-tool-list-view");
            listView.virtualizationMethod = CollectionVirtualizationMethod.DynamicHeight;
            listView.selectionType = SelectionType.None;
            listView.itemsSource = _toolListRows;
            listView.makeItem = _rowBinder.CreateToolListRowElement;
            listView.bindItem = _rowBinder.BindToolListRowElement;
            listView.unbindItem = _rowBinder.UnbindToolListRowElement;
            return listView;
        }

        private ToolListRowData FindToolRowByName(string toolName)
        {
            int toolIndex = FindToolRowIndex(toolName);
            return toolIndex < 0 ? null : _toolListRows[toolIndex];
        }

        private int FindToolRowIndex(string toolName)
        {
            for (int i = 0; i < _toolListRows.Count; i++)
            {
                ToolListRowData item = _toolListRows[i];
                if (item.IsTool && item.ToolName == toolName)
                {
                    return i;
                }
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

            _toolListRows.Insert(toolIndex + 1, ToolListRowData.CreateDetails(_toolListRows[toolIndex]));
        }

        private void RemoveToolDetailsRows()
        {
            for (int i = _toolListRows.Count - 1; i >= 0; i--)
            {
                if (_toolListRows[i].IsDetails)
                {
                    _toolListRows.RemoveAt(i);
                }
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
                if (!_toolListRows[i].IsDetails)
                {
                    count++;
                }
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
    }
}
