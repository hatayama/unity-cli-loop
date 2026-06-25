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
        public void Update_LoadedData_ShowsInfoToggleWhenDescriptionExists()
        {
            // Tests that rows with skill descriptions expose the help toggle.
            VisualElement root = CreateRootElement();
            ToolSettingsSection section = new(root);
            ToolSettingsSectionData data = CreateData(
                compileEnabled: true,
                includeGetLogs: false,
                compileDescription: "Compile description");

            section.Update(data);

            VisualElement row = BindToolListRow(root, 1);
            Button infoButton = row.Q<Button>("tool-list-row-info-toggle");
            Assert.IsNotNull(infoButton);
            Assert.AreEqual(DisplayStyle.Flex, infoButton.style.display.value);
        }

        [Test]
        public void Update_LoadedData_HidesInfoToggleWhenDescriptionMissing()
        {
            // Tests that rows without skill descriptions do not show an inert help toggle.
            VisualElement root = CreateRootElement();
            ToolSettingsSection section = new(root);
            ToolSettingsSectionData data = CreateData(
                compileEnabled: true,
                includeGetLogs: false);

            section.Update(data);

            VisualElement row = BindToolListRow(root, 1);
            Button infoButton = row.Q<Button>("tool-list-row-info-toggle");
            Assert.IsNotNull(infoButton);
            Assert.AreEqual(DisplayStyle.None, infoButton.style.display.value);
        }

        [Test]
        public void ToggleDescription_WhenSameToolSelectedTwice_ShowsThenHidesDescriptionPopup()
        {
            // Tests that the help toggle opens and closes the description popup.
            VisualElement root = CreateRootElement();
            ToolSettingsSection section = new(root);
            ToolSettingsSectionData data = CreateData(
                compileEnabled: true,
                includeGetLogs: false,
                compileDescription: "Compile description");

            section.Update(data);
            VisualElement popup = root.Q<VisualElement>("tool-description-popup");

            section.ToggleDescriptionPopupForTool("compile");

            Assert.AreEqual(DisplayStyle.Flex, popup.style.display.value);
            Assert.AreEqual("compile", root.Q<Label>("tool-description-popup-title").text);
            Assert.AreEqual("Compile description", root.Q<Label>("tool-description-popup-body").text);

            section.ToggleDescriptionPopupForTool("compile");

            Assert.AreEqual(DisplayStyle.None, popup.style.display.value);
        }

        [Test]
        public void ToggleDescription_WhenAnotherToolIsSelected_SwitchesDescriptionPopup()
        {
            // Tests that selecting another help toggle moves the popup selection.
            VisualElement root = CreateRootElement();
            ToolSettingsSection section = new(root);
            ToolSettingsSectionData data = CreateData(
                compileEnabled: true,
                includeGetLogs: true,
                compileDescription: "Compile description",
                getLogsDescription: "Logs description");

            section.Update(data);
            ListView listView = GetToolListView(root);
            VisualElement compileRow = BindToolListRow(root, 1);
            VisualElement getLogsRow = BindToolListRow(root, 2);
            Button getLogsInfoButton = getLogsRow.Q<Button>("tool-list-row-info-toggle");

            section.ToggleDescriptionPopupForTool("compile");
            section.ToggleDescriptionPopupForTool("get-logs");
            listView.bindItem(compileRow, 1);
            listView.bindItem(getLogsRow, 2);

            Assert.AreEqual("get-logs", root.Q<Label>("tool-description-popup-title").text);
            Assert.AreEqual("Logs description", root.Q<Label>("tool-description-popup-body").text);
            Assert.IsFalse(compileRow.Q<Button>("tool-list-row-info-toggle").ClassListContains("unity-cli-loop-tool-toggle-row__info-toggle--selected"));
            Assert.IsTrue(getLogsInfoButton.ClassListContains("unity-cli-loop-tool-toggle-row__info-toggle--selected"));
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
            Foldout foldout = new()            {
                name = "tool-settings-foldout"
            };
            VisualElement container = new()            {
                name = "tool-list-container"
            };

            VisualElement toolSettingsInfoContainer = new()            {
                name = "tool-settings-info-container"
            };

            foldout.Add(toolSettingsInfoContainer);
            foldout.Add(container);
            root.Add(foldout);
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
