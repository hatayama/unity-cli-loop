using System.Collections;
using NUnit.Framework;
using UnityEngine.UIElements;

using io.github.hatayama.UnityCliLoop.Presentation;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies Tool Settings Section behavior.
    /// </summary>
    [TestFixture]
    public class ToolSettingsSectionTests
    {
        private const int ToolListRowHeight = 24;
        private const int ToolDetailsRowHeight = 132;

        [Test]
        public void Constructor_CreatesSelectableToolSettingsInfoText()
        {
            // Tests that the tool settings info text can be selected without being edited.
            VisualElement root = CreateRootElement();
            _ = new ToolSettingsSection(root);

            TextField infoText = root.Q<TextField>("tool-settings-info-text");
            Assert.IsNotNull(infoText);
            Assert.AreEqual("Enable or disable tools. Disabled tools are hidden from AI agents.", infoText.value);
            Assert.IsTrue(infoText.isReadOnly);
            Assert.IsTrue(infoText.multiline);
        }

        [Test]
        public void Update_ClosedWithoutToolListData_DoesNotCreateToolRows()
        {
            VisualElement root = CreateRootElement();
            ToolSettingsSection section = new(root);
            ToolSettingsSectionData data = CreateData(
                compileEnabled: true,
                includeGetLogs: true,
                showToolSettings: false,
                hasToolListData: false);

            section.Update(data);

            IList items = GetToolListItems(root);
            ListView listView = GetToolListView(root);
            Assert.AreEqual(0, items.Count);
            Assert.AreEqual(DisplayStyle.None, listView.style.display.value);
        }

        [Test]
        public void Update_OpenWithoutToolListData_ShowsLoadingWithoutToolRows()
        {
            VisualElement root = CreateRootElement();
            ToolSettingsSection section = new(root);
            ToolSettingsSectionData data = CreateData(
                compileEnabled: true,
                includeGetLogs: true,
                showToolSettings: true,
                hasToolListData: false);

            section.Update(data);

            IList items = GetToolListItems(root);
            Label statusLabel = root.Q<Label>("tool-list-status-label");
            ListView listView = GetToolListView(root);
            Assert.AreEqual(0, items.Count);
            Assert.AreEqual("Loading tools...", statusLabel.text);
            Assert.AreEqual(DisplayStyle.Flex, statusLabel.style.display.value);
            Assert.AreEqual(DisplayStyle.None, listView.style.display.value);
        }

        [Test]
        public void Update_LoadedData_PopulatesVirtualizedRows()
        {
            VisualElement root = CreateRootElement();
            ToolSettingsSection section = new(root);
            ToolSettingsSectionData data = CreateData(
                compileEnabled: true,
                includeGetLogs: true,
                includeThirdPartyTool: true);

            section.Update(data);

            IList items = GetToolListItems(root);
            ListView listView = GetToolListView(root);
            Label statusLabel = root.Q<Label>("tool-list-status-label");
            Assert.AreEqual(5, items.Count);
            Assert.AreEqual(DisplayStyle.Flex, listView.style.display.value);
            Assert.AreEqual(DisplayStyle.None, statusLabel.style.display.value);
            Assert.AreEqual((5 * ToolListRowHeight) + 2, listView.style.height.value.value);
        }

        [Test]
        public void Update_LoadedData_ShowsDetailsButtonWhenDescriptionExists()
        {
            // Tests that rows with skill descriptions expose the details button.
            VisualElement root = CreateRootElement();
            ToolSettingsSection section = new(root);
            ToolSettingsSectionData data = CreateData(
                compileEnabled: true,
                includeGetLogs: false,
                compileDescription: "Compile description");

            section.Update(data);

            VisualElement row = BindToolListRow(root, 1);
            Button detailsButton = row.Q<Button>("tool-list-row-details-button");
            Assert.IsNotNull(detailsButton);
            Assert.AreEqual(DisplayStyle.Flex, detailsButton.style.display.value);
            Assert.AreEqual("Show Details", detailsButton.text);
        }

        [Test]
        public void Update_LoadedData_HidesDetailsButtonWhenDescriptionMissing()
        {
            // Tests that rows without skill descriptions do not show an inert details button.
            VisualElement root = CreateRootElement();
            ToolSettingsSection section = new(root);
            ToolSettingsSectionData data = CreateData(
                compileEnabled: true,
                includeGetLogs: false);

            section.Update(data);

            VisualElement row = BindToolListRow(root, 1);
            Button detailsButton = row.Q<Button>("tool-list-row-details-button");
            Assert.IsNotNull(detailsButton);
            Assert.AreEqual(DisplayStyle.None, detailsButton.style.display.value);
        }

        [Test]
        public void ToggleDetails_WhenSameToolSelectedTwice_ShowsThenHidesDetailsRow()
        {
            // Tests that the details button opens and closes a details row below the tool.
            VisualElement root = CreateRootElement();
            ToolSettingsSection section = new(root);
            ToolSettingsSectionData data = CreateData(
                compileEnabled: true,
                includeGetLogs: false,
                compileDescription: "Compile description");

            section.Update(data);
            IList items = GetToolListItems(root);
            ListView listView = GetToolListView(root);

            section.ToggleToolDetailsForTool("compile");

            VisualElement toolRow = BindToolListRow(root, 1);
            VisualElement detailsRow = BindToolListRow(root, 2);
            Button detailsButton = toolRow.Q<Button>("tool-list-row-details-button");
            Assert.AreEqual(3, items.Count);
            Assert.AreEqual("Hide Details", detailsButton.text);
            Assert.IsTrue(detailsButton.ClassListContains("unity-cli-loop-tool-toggle-row__details-button--selected"));
            TextField detailsBody = detailsRow.Q<TextField>("tool-list-row-details-body");
            Assert.AreEqual("Compile description", detailsBody.value);
            Assert.IsTrue(detailsBody.isReadOnly);
            Assert.AreEqual((2 * ToolListRowHeight) + ToolDetailsRowHeight + 2, listView.style.height.value.value);

            section.ToggleToolDetailsForTool("compile");
            listView.bindItem(toolRow, 1);

            Assert.AreEqual(2, items.Count);
            Assert.AreEqual("Show Details", toolRow.Q<Button>("tool-list-row-details-button").text);
        }

        [Test]
        public void ToggleDetails_WhenShown_AddsDetailsRowBelowTool()
        {
            // Tests that details appear as an expanded row immediately below the selected tool.
            VisualElement root = CreateRootElement();
            ToolSettingsSection section = new(root);
            ToolSettingsSectionData data = CreateData(
                compileEnabled: true,
                includeGetLogs: true,
                compileDescription: "Compile description");

            section.Update(data);
            section.ToggleToolDetailsForTool("compile");

            VisualElement detailsRow = BindToolListRow(root, 2);
            VisualElement followingToolRow = BindToolListRow(root, 3);

            TextField detailsBody = detailsRow.Q<TextField>("tool-list-row-details-body");
            Assert.AreEqual("Compile description", detailsBody.value);
            Assert.IsTrue(detailsBody.isReadOnly);
            Assert.AreEqual("get-logs", followingToolRow.Q<Label>("tool-list-row-label").text);
        }

        [Test]
        public void ToggleDetails_WhenAnotherToolIsSelected_SwitchesDetailsRow()
        {
            // Tests that selecting another details button moves the expanded details row.
            VisualElement root = CreateRootElement();
            ToolSettingsSection section = new(root);
            ToolSettingsSectionData data = CreateData(
                compileEnabled: true,
                includeGetLogs: true,
                compileDescription: "Compile description",
                getLogsDescription: "Logs description");

            section.Update(data);
            ListView listView = GetToolListView(root);

            section.ToggleToolDetailsForTool("compile");
            section.ToggleToolDetailsForTool("get-logs");
            VisualElement compileRow = BindToolListRow(root, 1);
            VisualElement getLogsRow = BindToolListRow(root, 2);
            VisualElement detailsRow = BindToolListRow(root, 3);
            listView.bindItem(compileRow, 1);
            listView.bindItem(getLogsRow, 2);

            TextField detailsBody = detailsRow.Q<TextField>("tool-list-row-details-body");
            Assert.AreEqual("Logs description", detailsBody.value);
            Assert.IsTrue(detailsBody.isReadOnly);
            Assert.AreEqual("Show Details", compileRow.Q<Button>("tool-list-row-details-button").text);
            Assert.AreEqual("Hide Details", getLogsRow.Q<Button>("tool-list-row-details-button").text);
            Assert.IsFalse(compileRow.Q<Button>("tool-list-row-details-button").ClassListContains("unity-cli-loop-tool-toggle-row__details-button--selected"));
            Assert.IsTrue(getLogsRow.Q<Button>("tool-list-row-details-button").ClassListContains("unity-cli-loop-tool-toggle-row__details-button--selected"));
        }

        [Test]
        public void Update_HeaderOnlyRefreshAfterLoad_PreservesLoadedRows()
        {
            VisualElement root = CreateRootElement();
            ToolSettingsSection section = new(root);
            ToolSettingsSectionData loadedData = CreateData(
                compileEnabled: true,
                includeGetLogs: false,
                includeThirdPartyTool: true);
            ToolSettingsSectionData headerOnlyData = CreateData(
                compileEnabled: false,
                includeGetLogs: false,
                includeThirdPartyTool: true,
                hasToolListData: false);

            section.Update(loadedData);
            section.Update(headerOnlyData);

            IList items = GetToolListItems(root);
            ListView listView = GetToolListView(root);
            Assert.AreEqual(4, items.Count);
            Assert.AreEqual(DisplayStyle.Flex, listView.style.display.value);
        }

        [Test]
        public void Update_ClosedAfterLoad_ReleasesLoadedRows()
        {
            VisualElement root = CreateRootElement();
            ToolSettingsSection section = new(root);
            ToolSettingsSectionData loadedData = CreateData(
                compileEnabled: true,
                includeGetLogs: false,
                includeThirdPartyTool: true);
            ToolSettingsSectionData closedData = CreateData(
                compileEnabled: true,
                includeGetLogs: false,
                showToolSettings: false,
                hasToolListData: false);

            section.Update(loadedData);
            section.Update(closedData);

            IList items = GetToolListItems(root);
            ListView listView = GetToolListView(root);
            Assert.AreEqual(0, items.Count);
            Assert.AreEqual(DisplayStyle.None, listView.style.display.value);
        }

        private static VisualElement CreateRootElement()
        {
            VisualElement root = new();
            VisualElement windowRoot = new()
            {
                name = "window-root"
            };
            Foldout foldout = new()
            {
                name = "tool-settings-foldout"
            };
            VisualElement container = new()
            {
                name = "tool-list-container"
            };

            VisualElement toolSettingsInfoContainer = new()
            {
                name = "tool-settings-info-container"
            };

            foldout.Add(toolSettingsInfoContainer);
            foldout.Add(container);
            windowRoot.Add(foldout);
            root.Add(windowRoot);
            return root;
        }

        private static ToolSettingsSectionData CreateData(
            bool compileEnabled,
            bool includeGetLogs,
            bool showToolSettings = true,
            bool includeThirdPartyTool = false,
            bool hasToolListData = true,
            string compileDescription = "",
            string getLogsDescription = "",
            string thirdPartyDescription = "")
        {
            ToolToggleItem compile = new(
                toolName: "compile",
                isEnabled: compileEnabled,
                isThirdParty: false,
                skillDescription: compileDescription);
            ToolToggleItem[] thirdPartyTools = includeThirdPartyTool
                ? new[]
                {
                    new ToolToggleItem(
                        toolName: "sample-third-party",
                        isEnabled: true,
                        isThirdParty: true,
                        skillDescription: thirdPartyDescription)
                }
                : System.Array.Empty<ToolToggleItem>();

            ToolToggleItem[] builtInTools;
            if (includeGetLogs)
            {
                ToolToggleItem getLogs = new(
                    toolName: "get-logs",
                    isEnabled: true,
                    isThirdParty: false,
                    skillDescription: getLogsDescription);
                builtInTools = new[] { compile, getLogs };
            }
            else
            {
                builtInTools = new[] { compile };
            }

            return new ToolSettingsSectionData(
                showToolSettings: showToolSettings,
                builtInTools: builtInTools,
                thirdPartyTools: thirdPartyTools,
                isRegistryAvailable: true,
                hasToolListData: hasToolListData);
        }

        private static IList GetToolListItems(VisualElement root)
        {
            ListView listView = GetToolListView(root);
            Assert.IsNotNull(listView.itemsSource);
            return listView.itemsSource;
        }

        private static VisualElement BindToolListRow(VisualElement root, int index)
        {
            ListView listView = GetToolListView(root);
            VisualElement row = listView.makeItem();
            listView.bindItem(row, index);
            return row;
        }

        private static ListView GetToolListView(VisualElement root)
        {
            ListView listView = root.Q<ListView>("tool-list-view");
            Assert.IsNotNull(listView);
            return listView;
        }
    }
}
