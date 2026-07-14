using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace io.github.hatayama.UnityCliLoop.Presentation
{
    /// <summary>
    /// Creates and binds virtualized tool-list row VisualElements for Tool Settings.
    /// </summary>
    internal sealed class ToolSettingsSectionRowBinder
    {
        private readonly ToolSettingsSection _owner;
        private readonly Action<string, bool> _onToolToggled;
        private readonly IReadOnlyList<ToolListRowData> _toolListRows;
        private readonly Dictionary<string, Toggle> _togglesByToolName;
        private readonly Func<string> _getExpandedDetailsToolName;

        internal ToolSettingsSectionRowBinder(
            ToolSettingsSection owner,
            Action<string, bool> onToolToggled,
            IReadOnlyList<ToolListRowData> toolListRows,
            Dictionary<string, Toggle> togglesByToolName,
            Func<string> getExpandedDetailsToolName)
        {
            Debug.Assert(owner != null, "owner must not be null");
            Debug.Assert(onToolToggled != null, "onToolToggled must not be null");
            Debug.Assert(toolListRows != null, "toolListRows must not be null");
            Debug.Assert(togglesByToolName != null, "togglesByToolName must not be null");
            Debug.Assert(getExpandedDetailsToolName != null, "getExpandedDetailsToolName must not be null");

            _owner = owner;
            _onToolToggled = onToolToggled;
            _toolListRows = toolListRows;
            _togglesByToolName = togglesByToolName;
            _getExpandedDetailsToolName = getExpandedDetailsToolName;
        }

        internal VisualElement CreateToolListRowElement()
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

                _onToolToggled.Invoke(item.ToolName, evt.newValue);
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
                _onToolToggled.Invoke(item.ToolName, newValue);
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

        internal void BindToolListRowElement(VisualElement row, int index)
        {
            Debug.Assert(index >= 0 && index < _toolListRows.Count, "tool list index must be valid");
            ToolListRowData item = _toolListRows[index];
            item.Owner = _owner;
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

        private static void BindHeaderRow(
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
            bool isDetailsExpanded = hasDescription && _getExpandedDetailsToolName() == item.ToolName;
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

        internal void UnbindToolListRowElement(VisualElement row, int index)
        {
            if (row.userData is ToolListRowData item && item.IsTool)
            {
                _togglesByToolName.Remove(item.ToolName);
            }

            row.userData = null;
        }

    }
}
