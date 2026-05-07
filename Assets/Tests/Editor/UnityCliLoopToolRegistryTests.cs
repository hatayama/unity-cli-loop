using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.Infrastructure;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test support type used by editor and play mode fixtures.
    /// </summary>
    internal static class ToolRegistryTestFactory
    {
        public static UnityCliLoopToolRegistry Create()
        {
            return new UnityCliLoopToolRegistry();
        }
    }

    /// <summary>
    /// Test fixture that verifies Unity CLI Loop Tool Registry behavior.
    /// </summary>
    [TestFixture]
    public sealed class UnityCliLoopToolRegistryTests
    {
        private const string ClearConsoleAssemblyName = "UnityCLILoop.FirstPartyTools.ClearConsole.Editor";
        private const string CompileAssemblyName = "UnityCLILoop.FirstPartyTools.Compile.Editor";
        private const string ControlPlayModeAssemblyName = "UnityCLILoop.FirstPartyTools.ControlPlayMode.Editor";
        private const string ExecuteDynamicCodeAssemblyName = "UnityCLILoop.FirstPartyTools.ExecuteDynamicCode.Editor";
        private const string FindGameObjectsAssemblyName = "UnityCLILoop.FirstPartyTools.FindGameObjects.Editor";
        private const string GetHierarchyAssemblyName = "UnityCLILoop.FirstPartyTools.GetHierarchy.Editor";
        private const string GetLogsAssemblyName = "UnityCLILoop.FirstPartyTools.GetLogs.Editor";
        private const string RecordInputAssemblyName = "UnityCLILoop.FirstPartyTools.RecordInput.Editor";
        private const string ReplayInputAssemblyName = "UnityCLILoop.FirstPartyTools.ReplayInput.Editor";
        private const string RunTestsAssemblyName = "UnityCLILoop.FirstPartyTools.RunTests.Editor";
        private const string ScreenshotAssemblyName = "UnityCLILoop.FirstPartyTools.Screenshot.Editor";
        private const string SimulateKeyboardAssemblyName = "UnityCLILoop.FirstPartyTools.SimulateKeyboard.Editor";
        private const string SimulateMouseInputAssemblyName = "UnityCLILoop.FirstPartyTools.SimulateMouseInput.Editor";
        private const string SimulateMouseUiAssemblyName = "UnityCLILoop.FirstPartyTools.SimulateMouseUi.Editor";

        [Test]
        public void Constructor_WhenFirstPartyToolsUseToolAttribute_RegistersThem()
        {
            // Tests that bundled tools use the same attribute-based registry path as extension tools.
            UnityCliLoopToolRegistry registry = ToolRegistryTestFactory.Create();

            Assert.That(registry.IsToolRegistered("compile"), Is.True);
            Assert.That(registry.IsToolRegistered("get-logs"), Is.True);
            Assert.That(registry.IsToolRegistered("execute-dynamic-code"), Is.True);
            Assert.That(registry.IsToolRegistered("clear-console"), Is.True);
            Assert.That(registry.IsToolRegistered("get-hierarchy"), Is.True);
            Assert.That(registry.IsToolRegistered("run-tests"), Is.True);
            Assert.That(registry.IsToolRegistered("find-game-objects"), Is.True);
            Assert.That(registry.IsToolRegistered("screenshot"), Is.True);
            Assert.That(registry.IsToolRegistered("record-input"), Is.True);
            Assert.That(registry.IsToolRegistered("replay-input"), Is.True);
            Assert.That(registry.IsToolRegistered("simulate-keyboard"), Is.True);
            Assert.That(registry.IsToolRegistered("simulate-mouse-input"), Is.True);
            Assert.That(registry.IsToolRegistered("simulate-mouse-ui"), Is.True);
        }

        [Test]
        public void GetToolType_WhenGetLogsComesFromFirstPartyToolsAssembly_ReturnsBundledPluginType()
        {
            // Tests that get-logs is a bundled plugin instead of an application-layer tool.
            UnityCliLoopToolRegistry registry = ToolRegistryTestFactory.Create();

            System.Type toolType = registry.GetToolType("get-logs");

            Assert.That(toolType, Is.Not.Null);
            Assert.That(toolType.Assembly.GetName().Name, Is.EqualTo(GetLogsAssemblyName));
            Assert.That(registry.IsThirdPartyTool("get-logs"), Is.False);
        }

        [Test]
        public void GetToolType_WhenCompileComesFromFirstPartyToolsAssembly_ReturnsBundledPluginType()
        {
            // Tests that compile is a bundled plugin instead of an application-layer tool.
            UnityCliLoopToolRegistry registry = ToolRegistryTestFactory.Create();

            System.Type toolType = registry.GetToolType("compile");

            Assert.That(toolType, Is.Not.Null);
            Assert.That(toolType.Assembly.GetName().Name, Is.EqualTo(CompileAssemblyName));
            Assert.That(registry.IsThirdPartyTool("compile"), Is.False);
        }

        [Test]
        public void GetToolType_WhenExecuteDynamicCodeComesFromFirstPartyToolsAssembly_ReturnsBundledPluginType()
        {
            // Tests that execute-dynamic-code is a bundled plugin instead of an application-layer tool.
            UnityCliLoopToolRegistry registry = ToolRegistryTestFactory.Create();

            System.Type toolType = registry.GetToolType("execute-dynamic-code");

            Assert.That(toolType, Is.Not.Null);
            Assert.That(toolType.Assembly.GetName().Name, Is.EqualTo(ExecuteDynamicCodeAssemblyName));
            Assert.That(registry.IsThirdPartyTool("execute-dynamic-code"), Is.False);
        }

        [Test]
        public void GetToolType_WhenToolComesFromFirstPartyToolsAssembly_ReturnsBundledPluginType()
        {
            // Tests that a bundled tool can live in the first-party plugin assembly and still register normally.
            UnityCliLoopToolRegistry registry = ToolRegistryTestFactory.Create();

            System.Type toolType = registry.GetToolType("control-play-mode");

            Assert.That(toolType, Is.Not.Null);
            Assert.That(toolType.Assembly.GetName().Name, Is.EqualTo(ControlPlayModeAssemblyName));
            Assert.That(registry.IsThirdPartyTool("control-play-mode"), Is.False);
        }

        [Test]
        public void GetToolType_WhenClearConsoleComesFromFirstPartyToolsAssembly_ReturnsBundledPluginType()
        {
            // Tests that clear-console is a bundled plugin instead of an application-layer tool.
            UnityCliLoopToolRegistry registry = ToolRegistryTestFactory.Create();

            System.Type toolType = registry.GetToolType("clear-console");

            Assert.That(toolType, Is.Not.Null);
            Assert.That(toolType.Assembly.GetName().Name, Is.EqualTo(ClearConsoleAssemblyName));
            Assert.That(registry.IsThirdPartyTool("clear-console"), Is.False);
        }

        [Test]
        public void GetToolType_WhenGetHierarchyComesFromFirstPartyToolsAssembly_ReturnsBundledPluginType()
        {
            // Tests that get-hierarchy is a bundled plugin instead of an application-layer tool.
            UnityCliLoopToolRegistry registry = ToolRegistryTestFactory.Create();

            System.Type toolType = registry.GetToolType("get-hierarchy");

            Assert.That(toolType, Is.Not.Null);
            Assert.That(toolType.Assembly.GetName().Name, Is.EqualTo(GetHierarchyAssemblyName));
            Assert.That(registry.IsThirdPartyTool("get-hierarchy"), Is.False);
        }

        [Test]
        public void GetToolType_WhenRunTestsComesFromFirstPartyToolsAssembly_ReturnsBundledPluginType()
        {
            // Tests that run-tests is a bundled plugin instead of an application-layer tool.
            UnityCliLoopToolRegistry registry = ToolRegistryTestFactory.Create();

            System.Type toolType = registry.GetToolType("run-tests");

            Assert.That(toolType, Is.Not.Null);
            Assert.That(toolType.Assembly.GetName().Name, Is.EqualTo(RunTestsAssemblyName));
            Assert.That(registry.IsThirdPartyTool("run-tests"), Is.False);
        }

        [Test]
        public void GetToolType_WhenFindGameObjectsComesFromFirstPartyToolsAssembly_ReturnsBundledPluginType()
        {
            // Tests that find-game-objects is a bundled plugin instead of an application-layer tool.
            UnityCliLoopToolRegistry registry = ToolRegistryTestFactory.Create();

            System.Type toolType = registry.GetToolType("find-game-objects");

            Assert.That(toolType, Is.Not.Null);
            Assert.That(toolType.Assembly.GetName().Name, Is.EqualTo(FindGameObjectsAssemblyName));
            Assert.That(registry.IsThirdPartyTool("find-game-objects"), Is.False);
        }

        [Test]
        public void GetToolType_WhenScreenshotComesFromFirstPartyToolsAssembly_ReturnsBundledPluginType()
        {
            // Tests that screenshot is a bundled plugin instead of an application-layer tool.
            UnityCliLoopToolRegistry registry = ToolRegistryTestFactory.Create();

            System.Type toolType = registry.GetToolType("screenshot");

            Assert.That(toolType, Is.Not.Null);
            Assert.That(toolType.Assembly.GetName().Name, Is.EqualTo(ScreenshotAssemblyName));
            Assert.That(registry.IsThirdPartyTool("screenshot"), Is.False);
        }

        [Test]
        public void GetToolType_WhenRecordInputComesFromFirstPartyToolsAssembly_ReturnsBundledPluginType()
        {
            // Tests that record-input is a bundled plugin instead of an application-layer tool.
            UnityCliLoopToolRegistry registry = ToolRegistryTestFactory.Create();

            System.Type toolType = registry.GetToolType("record-input");

            Assert.That(toolType, Is.Not.Null);
            Assert.That(toolType.Assembly.GetName().Name, Is.EqualTo(RecordInputAssemblyName));
            Assert.That(registry.IsThirdPartyTool("record-input"), Is.False);
        }

        [Test]
        public void GetToolType_WhenReplayInputComesFromFirstPartyToolsAssembly_ReturnsBundledPluginType()
        {
            // Tests that replay-input is a bundled plugin instead of an application-layer tool.
            UnityCliLoopToolRegistry registry = ToolRegistryTestFactory.Create();

            System.Type toolType = registry.GetToolType("replay-input");

            Assert.That(toolType, Is.Not.Null);
            Assert.That(toolType.Assembly.GetName().Name, Is.EqualTo(ReplayInputAssemblyName));
            Assert.That(registry.IsThirdPartyTool("replay-input"), Is.False);
        }

        [Test]
        public void GetToolType_WhenSimulateKeyboardComesFromFirstPartyToolsAssembly_ReturnsBundledPluginType()
        {
            // Tests that simulate-keyboard is a bundled plugin instead of an application-layer tool.
            UnityCliLoopToolRegistry registry = ToolRegistryTestFactory.Create();

            System.Type toolType = registry.GetToolType("simulate-keyboard");

            Assert.That(toolType, Is.Not.Null);
            Assert.That(toolType.Assembly.GetName().Name, Is.EqualTo(SimulateKeyboardAssemblyName));
            Assert.That(registry.IsThirdPartyTool("simulate-keyboard"), Is.False);
        }

        [Test]
        public void GetToolType_WhenSimulateMouseInputComesFromFirstPartyToolsAssembly_ReturnsBundledPluginType()
        {
            // Tests that simulate-mouse-input is a bundled plugin instead of an application-layer tool.
            UnityCliLoopToolRegistry registry = ToolRegistryTestFactory.Create();

            System.Type toolType = registry.GetToolType("simulate-mouse-input");

            Assert.That(toolType, Is.Not.Null);
            Assert.That(toolType.Assembly.GetName().Name, Is.EqualTo(SimulateMouseInputAssemblyName));
            Assert.That(registry.IsThirdPartyTool("simulate-mouse-input"), Is.False);
        }

        [Test]
        public void GetToolType_WhenSimulateMouseUiComesFromFirstPartyToolsAssembly_ReturnsBundledPluginType()
        {
            // Tests that simulate-mouse-ui is a bundled plugin instead of an application-layer tool.
            UnityCliLoopToolRegistry registry = ToolRegistryTestFactory.Create();

            System.Type toolType = registry.GetToolType("simulate-mouse-ui");

            Assert.That(toolType, Is.Not.Null);
            Assert.That(toolType.Assembly.GetName().Name, Is.EqualTo(SimulateMouseUiAssemblyName));
            Assert.That(registry.IsThirdPartyTool("simulate-mouse-ui"), Is.False);
        }

        [Test]
        public void Constructor_WhenFocusWindowIsNativeCliCommand_DoesNotRegisterItAsTool()
        {
            // Tests that focus-window stays a native CLI command instead of an extension-facing Unity tool.
            UnityCliLoopToolRegistry registry = ToolRegistryTestFactory.Create();

            Assert.That(registry.IsToolRegistered("focus-window"), Is.False);
        }

        [Test]
        public void Constructor_WhenGetVersionIsInternalBridgeCommand_DoesNotRegisterItAsTool()
        {
            // Tests that get-version is kept out of the extension-facing runtime registry.
            UnityCliLoopToolRegistry registry = ToolRegistryTestFactory.Create();

            Assert.That(registry.IsToolRegistered(UnityCliLoopConstants.COMMAND_NAME_GET_VERSION), Is.False);
        }

        [Test]
        public void Constructor_WhenGetToolDetailsIsInternalBridgeCommand_DoesNotRegisterItAsTool()
        {
            // Tests that get-tool-details is kept out of the extension-facing runtime registry.
            UnityCliLoopToolRegistry registry = ToolRegistryTestFactory.Create();

            Assert.That(registry.IsToolRegistered(UnityCliLoopConstants.COMMAND_NAME_GET_TOOL_DETAILS), Is.False);
        }

        [Test]
        public async Task ExecuteCommandAsync_WhenCommandIsGetVersion_ReturnsBridgeVersionPayload()
        {
            // Tests that get-version still works as a CLI-only bridge command after leaving the tool registry.
            UnityCliLoopToolResponse response = await UnityApiHandler.ExecuteCommandAsync(
                UnityCliLoopConstants.COMMAND_NAME_GET_VERSION,
                new JObject());

            GetVersionResponse getVersionResponse = response as GetVersionResponse;
            Assert.That(getVersionResponse, Is.Not.Null);
            Assert.That(getVersionResponse.Ver, Is.EqualTo(UnityCliLoopVersion.VERSION));
            Assert.That(getVersionResponse.UnityVersion, Is.Not.Empty);
            Assert.That(getVersionResponse.IsEditor, Is.True);
        }

        [Test]
        public async Task ExecuteCommandAsync_WhenCommandIsGetToolDetails_ReturnsCatalogWithoutInternalCommands()
        {
            // Tests that CLI catalog access still works without registering the catalog command as a tool.
            UnityCliLoopToolResponse response = await UnityApiHandler.ExecuteCommandAsync(
                UnityCliLoopConstants.COMMAND_NAME_GET_TOOL_DETAILS,
                new JObject());

            GetToolDetailsResponse getToolDetailsResponse = response as GetToolDetailsResponse;
            Assert.That(getToolDetailsResponse, Is.Not.Null);
            Assert.That(getToolDetailsResponse.Ver, Is.EqualTo(UnityCliLoopVersion.VERSION));

            string[] toolNames = getToolDetailsResponse.Tools
                .Select(tool => tool.Name)
                .ToArray();

            Assert.That(toolNames, Does.Contain("get-logs"));
            Assert.That(toolNames, Does.Not.Contain(UnityCliLoopConstants.COMMAND_NAME_GET_TOOL_DETAILS));
            Assert.That(toolNames, Does.Not.Contain(UnityCliLoopConstants.COMMAND_NAME_GET_VERSION));
            Assert.That(toolNames, Does.Not.Contain("focus-window"));
            Assert.That(toolNames, Does.Not.Contain("ping"));
            Assert.That(toolNames, Does.Not.Contain("debug-sleep"));
        }

        [Test]
        public void Constructor_WhenLegacyDevelopmentToolsAreRemoved_DoesNotRegisterThem()
        {
            // Tests that legacy MCP-era development tools are not exposed through the runtime registry.
            UnityCliLoopToolRegistry registry = ToolRegistryTestFactory.Create();

            Assert.That(registry.IsToolRegistered("ping"), Is.False);
            Assert.That(registry.IsToolRegistered("debug-sleep"), Is.False);
        }

        [Test]
        public void Constructor_WhenSampleToolUsesToolContractsAssembly_RegistersAsThirdParty()
        {
            // Tests that a sample extension tool uses the same registry path while remaining outside first-party assemblies.
            UnityCliLoopToolRegistry registry = ToolRegistryTestFactory.Create();

            Assert.That(registry.IsToolRegistered("hello-world"), Is.True);
            Assert.That(registry.IsThirdPartyTool("hello-world"), Is.True);
        }

        [Test]
        public async Task ExecuteCommandAsync_WhenSampleToolUsesTypedContract_ReturnsTypedResponse()
        {
            // Tests that third-party sample tools execute through the same typed contract path as bundled tools.
            JObject parameters = JObject.FromObject(new
            {
                name = "Masamichi",
                language = "french",
                includeTimestamp = false
            });

            UnityCliLoopToolResponse response = await UnityApiHandler.ExecuteCommandAsync("hello-world", parameters);
            JObject serializedResponse = JObject.FromObject(response);

            Assert.That(serializedResponse.Value<string>("Message"), Is.EqualTo("Bonjour, Masamichi!"));
            Assert.That(serializedResponse.Value<string>("Language"), Is.EqualTo("french"));
            Assert.That(serializedResponse["Timestamp"]?.Type, Is.EqualTo(JTokenType.Null));
        }

        [Test]
        public async Task ExecuteToolAsync_WhenToolReturnsResponse_AssignsVersionToResponseInstance()
        {
            // Tests that response versioning is assigned per response instead of using global contract state.
            UnityCliLoopToolRegistry registry = ToolRegistryTestFactory.Create();
            JObject parameters = JObject.FromObject(new
            {
                name = "Masamichi",
                language = "english",
                includeTimestamp = false
            });

            UnityCliLoopToolResponse response = await registry.ExecuteToolAsync("hello-world", parameters);

            Assert.That(response.Ver, Is.EqualTo(UnityCliLoopVersion.VERSION));
        }

        [Test]
        public void CustomCommandSamplesAsmdef_ReferencesOnlyToolContracts()
        {
            // Tests that third-party sample tools depend only on the public tool contract assembly.
            string asmdefPath = Path.Combine(
                UnityCliLoopPathResolver.GetProjectRoot(),
                "Assets",
                "Editor",
                "CustomCommandSamples",
                "UnityCLILoop.CustomCommandSamples.Editor.asmdef");
            JObject asmdef = JObject.Parse(File.ReadAllText(asmdefPath));
            string[] references = asmdef["references"]?.Values<string>().ToArray() ?? new string[0];

            Assert.That(references, Does.Contain("UnityCLILoop.ToolContracts"));
            Assert.That(references, Does.Not.Contain("UnityCLILoop.Application"));
            Assert.That(references, Does.Not.Contain("UnityCLILoop.Domain"));
            Assert.That(references, Does.Not.Contain("UnityCLILoop.Infrastructure"));
            Assert.That(references, Does.Not.Contain("UnityCLILoop.Presentation"));
        }

        [Test]
        public void FirstPartyToolsAsmdef_DoesNotReferenceImplementationLayers()
        {
            // Tests that bundled plugin startup wiring does not depend on UnityCliLoop platform implementation layers.
            string asmdefPath = Path.Combine(
                UnityCliLoopPathResolver.GetProjectRoot(),
                "Packages",
                "src",
                "Editor",
                "FirstPartyTools",
                "UnityCLILoop.FirstPartyTools.Editor.asmdef");
            JObject asmdef = JObject.Parse(File.ReadAllText(asmdefPath));
            string[] references = asmdef["references"]?.Values<string>().ToArray() ?? new string[0];

            Assert.That(references, Does.Contain(ClearConsoleAssemblyName));
            Assert.That(references, Does.Contain(CompileAssemblyName));
            Assert.That(references, Does.Contain(ControlPlayModeAssemblyName));
            Assert.That(references, Does.Contain(ExecuteDynamicCodeAssemblyName));
            Assert.That(references, Does.Contain(FindGameObjectsAssemblyName));
            Assert.That(references, Does.Contain(GetHierarchyAssemblyName));
            Assert.That(references, Does.Contain(GetLogsAssemblyName));
            Assert.That(references, Does.Contain(RecordInputAssemblyName));
            Assert.That(references, Does.Contain(ReplayInputAssemblyName));
            Assert.That(references, Does.Contain(RunTestsAssemblyName));
            Assert.That(references, Does.Contain(ScreenshotAssemblyName));
            Assert.That(references, Does.Contain(SimulateKeyboardAssemblyName));
            Assert.That(references, Does.Contain(SimulateMouseInputAssemblyName));
            Assert.That(references, Does.Contain(SimulateMouseUiAssemblyName));
            Assert.That(references, Does.Not.Contain("UnityCLILoop.Application"));
            Assert.That(references, Does.Not.Contain("UnityCLILoop.Domain"));
            Assert.That(references, Does.Not.Contain("UnityCLILoop.Infrastructure"));
            Assert.That(references, Does.Not.Contain("UnityCLILoop.Presentation"));
        }

        [Test]
        public void GetRegisteredTools_WhenSerialized_DoesNotExposeDescription()
        {
            // Tests that get-tool-details no longer exposes display descriptions from runtime attributes.
            UnityCliLoopToolRegistry registry = ToolRegistryTestFactory.Create();
            ToolInfo tool = registry.GetRegisteredTools()
                .First(item => item.Name == "get-logs");
            JObject serializedTool = JObject.FromObject(tool);

            Assert.That(serializedTool.ContainsKey("description"), Is.False);
        }

        [Test]
        public void GetToolSettingsCatalog_WhenSerialized_DoesNotExposeDescription()
        {
            // Tests that Settings metadata no longer carries tooltip descriptions.
            UnityCliLoopToolRegistry registry = ToolRegistryTestFactory.Create();
            ToolSettingsCatalogItem tool = registry.GetToolSettingsCatalog()
                .First(item => item.Name == "get-logs");
            JObject serializedTool = JObject.FromObject(tool);

            Assert.That(serializedTool.ContainsKey("Description"), Is.False);
        }
    }
}
