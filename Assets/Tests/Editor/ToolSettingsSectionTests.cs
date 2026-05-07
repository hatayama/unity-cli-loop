using System.Collections;
using NUnit.Framework;
using UnityEngine.UIElements;

using io.github.hatayama.UnityCliLoop.Presentation;
using io.github.hatayama.UnityCliLoop.ToolContracts;

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

        [Test]
        public void Update_RestrictedLevel_MarksRestrictedButtonAsActive()
        {
            VisualElement root = CreateRootElement();
            ToolSettingsSection section = new(root);
            ToolSettingsSectionData data = CreateData(
                compileEnabled: true,
                includeGetLogs: false,
                dynamicCodeSecurityLevel: DynamicCodeSecurityLevel.Restricted);

            section.Update(data);

            Button restrictedButton = root.Q<Button>("security-level-restricted-button");
            Button fullAccessButton = root.Q<Button>("security-level-full-access-button");

            Assert.IsTrue(restrictedButton.ClassListContains("unity-cli-loop-segmented-control__button--active"));
            Assert.IsFalse(fullAccessButton.ClassListContains("unity-cli-loop-segmented-control__button--active"));
        }

        [Test]
        public void Update_FullAccessLevel_MarksFullAccessButtonAsWarningActive()
        {
            VisualElement root = CreateRootElement();
            ToolSettingsSection section = new(root);
            ToolSettingsSectionData data = CreateData(
                compileEnabled: true,
                includeGetLogs: false,
                dynamicCodeSecurityLevel: DynamicCodeSecurityLevel.FullAccess);

            section.Update(data);

            Button restrictedButton = root.Q<Button>("security-level-restricted-button");
            Button fullAccessButton = root.Q<Button>("security-level-full-access-button");
            Label description = root.Q<Label>("security-level-description");

            Assert.IsFalse(restrictedButton.ClassListContains("unity-cli-loop-segmented-control__button--active"));
            Assert.IsTrue(fullAccessButton.ClassListContains("unity-cli-loop-segmented-control__button--active"));
            Assert.IsTrue(fullAccessButton.ClassListContains("unity-cli-loop-segmented-control__button--warning-active"));
            Assert.IsTrue(description.ClassListContains("unity-cli-loop-security-level-description--warning"));
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

            Label cliReferenceLink = new()            {
                name = "cli-reference-link"
            };
            Button securityLevelRestrictedButton = new()            {
                name = "security-level-restricted-button"
            };
            Button securityLevelFullAccessButton = new()            {
                name = "security-level-full-access-button"
            };
            Label securityLevelDescription = new()            {
                name = "security-level-description"
            };
            VisualElement toolSettingsInfoContainer = new()            {
                name = "tool-settings-info-container"
            };

            foldout.Add(securityLevelRestrictedButton);
            foldout.Add(securityLevelFullAccessButton);
            foldout.Add(securityLevelDescription);
            foldout.Add(toolSettingsInfoContainer);
            foldout.Add(container);
            root.Add(foldout);
            root.Add(cliReferenceLink);
            return root;
        }

        private static ToolSettingsSectionData CreateData(
            bool compileEnabled,
            bool includeGetLogs,
            bool showToolSettings = true,
            bool includeThirdPartyTool = false,
            bool hasToolListData = true,
            DynamicCodeSecurityLevel dynamicCodeSecurityLevel = DynamicCodeSecurityLevel.Restricted)
        {
            ToolToggleItem compile = new(
                toolName: "compile",
                isEnabled: compileEnabled,
                isThirdParty: false);
            ToolToggleItem[] thirdPartyTools = includeThirdPartyTool
                ? new[]
                {
                    new ToolToggleItem(
                        toolName: "sample-third-party",
                        isEnabled: true,
                        isThirdParty: true)
                }
                : System.Array.Empty<ToolToggleItem>();

            ToolToggleItem[] builtInTools;
            if (includeGetLogs)
            {
                ToolToggleItem getLogs = new(
                    toolName: "get-logs",
                    isEnabled: true,
                    isThirdParty: false);
                builtInTools = new[] { compile, getLogs };
            }
            else
            {
                builtInTools = new[] { compile };
            }

            return new ToolSettingsSectionData(
                showToolSettings: showToolSettings,
                dynamicCodeSecurityLevel: dynamicCodeSecurityLevel,
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

        private static ListView GetToolListView(VisualElement root)
        {
            ListView listView = root.Q<ListView>("tool-list-view");
            Assert.IsNotNull(listView);
            return listView;
        }
    }
}
