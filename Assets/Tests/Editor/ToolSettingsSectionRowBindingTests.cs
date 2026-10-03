using System;

using NUnit.Framework;
using UnityEngine.UIElements;

using io.github.hatayama.UnityCliLoop.Presentation;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies header row binding, row unbinding, single-toggle updates, and the height of an empty tool list in
    /// Tool Settings, driving the list view's own make, bind, and unbind callbacks.
    /// </summary>
    public sealed class ToolSettingsSectionRowBindingTests
    {
        private const int ToolListRowHeight = 24;

        /// <summary>
        /// Verifies a header row shows only its label and is styled as a header, and that binding the same row to a
        /// tool afterwards drops the header styling.
        /// </summary>
        [Test]
        public void BindItem_WithAHeaderRow_ShowsOnlyTheHeaderLabelUntilReboundToATool()
        {
            VisualElement root = CreateRootElement();
            ToolSettingsSection section = new ToolSettingsSection(root);
            section.Update(CreateData(Array.Empty<ToolToggleItem>()));
            ListView listView = GetToolListView(root);
            VisualElement row = listView.makeItem();

            listView.bindItem(row, 0);

            Assert.That(row.ClassListContains("unity-cli-loop-tool-list-row--header"), Is.True);
            Assert.That(row.Q<Toggle>("tool-list-row-toggle").style.display.value, Is.EqualTo(DisplayStyle.None));
            Assert.That(row.Q<Label>("tool-list-row-label").ClassListContains("unity-cli-loop-tool-group-header"), Is.True);

            listView.bindItem(row, 1);

            Assert.That(row.ClassListContains("unity-cli-loop-tool-list-row--header"), Is.False);
            Assert.That(row.Q<Label>("tool-list-row-label").text, Is.EqualTo("compile"));
        }

        /// <summary>
        /// Verifies a single-toggle update no longer reaches a tool row's toggle once the row is unbound.
        /// </summary>
        [Test]
        public void UpdateSingleToggle_AfterTheRowIsUnbound_NoLongerChangesItsToggle()
        {
            VisualElement root = CreateRootElement();
            ToolSettingsSection section = new ToolSettingsSection(root);
            section.Update(CreateData(Array.Empty<ToolToggleItem>()));
            ListView listView = GetToolListView(root);
            VisualElement row = listView.makeItem();
            listView.bindItem(row, 1);
            Toggle toggle = row.Q<Toggle>("tool-list-row-toggle");

            // The update must come right after the unbind: an earlier update refreshes the list view, which binds
            // its own row for the tool and takes the toggle over from this one.
            listView.unbindItem(row, 1);
            section.UpdateSingleToggle("compile", false);

            Assert.That(toggle.value, Is.True);
            Assert.That(row.userData, Is.Null);
        }

        /// <summary>
        /// Verifies a single-toggle update also changes the row data, so a row bound afterwards shows the new value.
        /// </summary>
        [Test]
        public void UpdateSingleToggle_BeforeTheRowIsBound_IsShownWhenItBinds()
        {
            VisualElement root = CreateRootElement();
            ToolSettingsSection section = new ToolSettingsSection(root);
            section.Update(CreateData(Array.Empty<ToolToggleItem>()));
            ListView listView = GetToolListView(root);

            section.UpdateSingleToggle("compile", false);
            VisualElement row = listView.makeItem();
            listView.bindItem(row, 1);

            Assert.That(row.Q<Toggle>("tool-list-row-toggle").value, Is.False);
        }

        /// <summary>
        /// Verifies unbinding a header row clears its data without touching any tool toggle.
        /// </summary>
        [Test]
        public void UnbindItem_WithAHeaderRow_ClearsItsData()
        {
            VisualElement root = CreateRootElement();
            ToolSettingsSection section = new ToolSettingsSection(root);
            section.Update(CreateData(Array.Empty<ToolToggleItem>()));
            ListView listView = GetToolListView(root);
            VisualElement headerRow = listView.makeItem();
            listView.bindItem(headerRow, 0);
            VisualElement toolRow = listView.makeItem();
            listView.bindItem(toolRow, 1);

            listView.unbindItem(headerRow, 0);
            section.UpdateSingleToggle("compile", false);

            Assert.That(headerRow.userData, Is.Null);
            Assert.That(toolRow.Q<Toggle>("tool-list-row-toggle").value, Is.False);
        }

        /// <summary>
        /// Verifies an available registry with no tools still reserves one row of height for the list.
        /// </summary>
        [Test]
        public void Update_WithNoToolsFromAnAvailableRegistry_KeepsOneRowOfHeight()
        {
            VisualElement root = CreateRootElement();
            ToolSettingsSection section = new ToolSettingsSection(root);

            section.Update(new ToolSettingsSectionData(
                showToolSettings: true,
                builtInTools: Array.Empty<ToolToggleItem>(),
                thirdPartyTools: Array.Empty<ToolToggleItem>(),
                isRegistryAvailable: true,
                hasToolListData: true));

            Assert.That(GetToolListView(root).style.height.value.value, Is.EqualTo(ToolListRowHeight + 2));
        }

        private static ToolSettingsSectionData CreateData(ToolToggleItem[] thirdPartyTools)
        {
            ToolToggleItem compile = new ToolToggleItem(
                toolName: "compile",
                isEnabled: true,
                isThirdParty: false,
                skillDescription: string.Empty);
            return new ToolSettingsSectionData(
                showToolSettings: true,
                builtInTools: new[] { compile },
                thirdPartyTools: thirdPartyTools,
                isRegistryAvailable: true,
                hasToolListData: true);
        }

        private static VisualElement CreateRootElement()
        {
            VisualElement root = new VisualElement();
            VisualElement windowRoot = new VisualElement { name = "window-root" };
            Foldout foldout = new Foldout { name = "tool-settings-foldout" };
            foldout.Add(new VisualElement { name = "tool-settings-info-container" });
            foldout.Add(new VisualElement { name = "tool-list-container" });
            windowRoot.Add(foldout);
            root.Add(windowRoot);
            return root;
        }

        private static ListView GetToolListView(VisualElement root)
        {
            ListView listView = root.Q<ListView>("tool-list-view");
            Assert.That(listView, Is.Not.Null);
            return listView;
        }
    }
}
