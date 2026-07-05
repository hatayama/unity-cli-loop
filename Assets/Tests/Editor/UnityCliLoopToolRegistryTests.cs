using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.CompositionRoot;
using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.Infrastructure;
using io.github.hatayama.UnityCliLoop.ToolContracts;
using ApplicationRegistrar = io.github.hatayama.UnityCliLoop.Application.UnityCliLoopToolRegistrar;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test support type used by editor and play mode fixtures.
    /// </summary>
    internal static class ToolRegistryTestFactory
    {
        public static UnityCliLoopToolRegistry Create()
        {
            return new UnityCliLoopToolRegistry(
                new ToolSettingsRepository(),
                internalToolNameProvider: null,
                toolDiscovery: UnityCliLoopToolDiscovery.DiscoverTools);
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
        private const string PausePointAssemblyName = "UnityCLILoop.FirstPartyTools.PausePoint.Editor";
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
            Assert.That(registry.IsToolRegistered(UnityCliLoopConstants.TOOL_NAME_ENABLE_PAUSE_POINT), Is.True);
            Assert.That(registry.IsToolRegistered(UnityCliLoopConstants.TOOL_NAME_CLEAR_PAUSE_POINT), Is.True);
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
            AssertThirdPartyTool(toolType, false);
        }

        [Test]
        public void GetToolType_WhenCompileComesFromFirstPartyToolsAssembly_ReturnsBundledPluginType()
        {
            // Tests that compile is a bundled plugin instead of an application-layer tool.
            UnityCliLoopToolRegistry registry = ToolRegistryTestFactory.Create();

            System.Type toolType = registry.GetToolType("compile");

            Assert.That(toolType, Is.Not.Null);
            Assert.That(toolType.Assembly.GetName().Name, Is.EqualTo(CompileAssemblyName));
            AssertThirdPartyTool(toolType, false);
        }

        [Test]
        public void GetToolType_WhenExecuteDynamicCodeComesFromFirstPartyToolsAssembly_ReturnsBundledPluginType()
        {
            // Tests that execute-dynamic-code is a bundled plugin instead of an application-layer tool.
            UnityCliLoopToolRegistry registry = ToolRegistryTestFactory.Create();

            System.Type toolType = registry.GetToolType("execute-dynamic-code");

            Assert.That(toolType, Is.Not.Null);
            Assert.That(toolType.Assembly.GetName().Name, Is.EqualTo(ExecuteDynamicCodeAssemblyName));
            AssertThirdPartyTool(toolType, false);
        }

        [Test]
        public void GetToolType_WhenToolComesFromFirstPartyToolsAssembly_ReturnsBundledPluginType()
        {
            // Tests that a bundled tool can live in the first-party plugin assembly and still register normally.
            UnityCliLoopToolRegistry registry = ToolRegistryTestFactory.Create();

            System.Type toolType = registry.GetToolType("control-play-mode");

            Assert.That(toolType, Is.Not.Null);
            Assert.That(toolType.Assembly.GetName().Name, Is.EqualTo(ControlPlayModeAssemblyName));
            AssertThirdPartyTool(toolType, false);
        }

        [Test]
        public void GetToolType_WhenClearConsoleComesFromFirstPartyToolsAssembly_ReturnsBundledPluginType()
        {
            // Tests that clear-console is a bundled plugin instead of an application-layer tool.
            UnityCliLoopToolRegistry registry = ToolRegistryTestFactory.Create();

            System.Type toolType = registry.GetToolType("clear-console");

            Assert.That(toolType, Is.Not.Null);
            Assert.That(toolType.Assembly.GetName().Name, Is.EqualTo(ClearConsoleAssemblyName));
            AssertThirdPartyTool(toolType, false);
        }

        [Test]
        public void GetToolType_WhenGetHierarchyComesFromFirstPartyToolsAssembly_ReturnsBundledPluginType()
        {
            // Tests that get-hierarchy is a bundled plugin instead of an application-layer tool.
            UnityCliLoopToolRegistry registry = ToolRegistryTestFactory.Create();

            System.Type toolType = registry.GetToolType("get-hierarchy");

            Assert.That(toolType, Is.Not.Null);
            Assert.That(toolType.Assembly.GetName().Name, Is.EqualTo(GetHierarchyAssemblyName));
            AssertThirdPartyTool(toolType, false);
        }

        [Test]
        public void GetToolType_WhenRunTestsComesFromFirstPartyToolsAssembly_ReturnsBundledPluginType()
        {
            // Tests that run-tests is a bundled plugin instead of an application-layer tool.
            UnityCliLoopToolRegistry registry = ToolRegistryTestFactory.Create();

            System.Type toolType = registry.GetToolType("run-tests");

            Assert.That(toolType, Is.Not.Null);
            Assert.That(toolType.Assembly.GetName().Name, Is.EqualTo(RunTestsAssemblyName));
            AssertThirdPartyTool(toolType, false);
        }

        [Test]
        public void GetToolType_WhenFindGameObjectsComesFromFirstPartyToolsAssembly_ReturnsBundledPluginType()
        {
            // Tests that find-game-objects is a bundled plugin instead of an application-layer tool.
            UnityCliLoopToolRegistry registry = ToolRegistryTestFactory.Create();

            System.Type toolType = registry.GetToolType("find-game-objects");

            Assert.That(toolType, Is.Not.Null);
            Assert.That(toolType.Assembly.GetName().Name, Is.EqualTo(FindGameObjectsAssemblyName));
            AssertThirdPartyTool(toolType, false);
        }

        [Test]
        public void GetToolType_WhenScreenshotComesFromFirstPartyToolsAssembly_ReturnsBundledPluginType()
        {
            // Tests that screenshot is a bundled plugin instead of an application-layer tool.
            UnityCliLoopToolRegistry registry = ToolRegistryTestFactory.Create();

            System.Type toolType = registry.GetToolType("screenshot");

            Assert.That(toolType, Is.Not.Null);
            Assert.That(toolType.Assembly.GetName().Name, Is.EqualTo(ScreenshotAssemblyName));
            AssertThirdPartyTool(toolType, false);
        }

        [Test]
        public void GetToolType_WhenRecordInputComesFromFirstPartyToolsAssembly_ReturnsBundledPluginType()
        {
            // Tests that record-input is a bundled plugin instead of an application-layer tool.
            UnityCliLoopToolRegistry registry = ToolRegistryTestFactory.Create();

            System.Type toolType = registry.GetToolType("record-input");

            Assert.That(toolType, Is.Not.Null);
            Assert.That(toolType.Assembly.GetName().Name, Is.EqualTo(RecordInputAssemblyName));
            AssertThirdPartyTool(toolType, false);
        }

        [Test]
        public void GetToolType_WhenReplayInputComesFromFirstPartyToolsAssembly_ReturnsBundledPluginType()
        {
            // Tests that replay-input is a bundled plugin instead of an application-layer tool.
            UnityCliLoopToolRegistry registry = ToolRegistryTestFactory.Create();

            System.Type toolType = registry.GetToolType("replay-input");

            Assert.That(toolType, Is.Not.Null);
            Assert.That(toolType.Assembly.GetName().Name, Is.EqualTo(ReplayInputAssemblyName));
            AssertThirdPartyTool(toolType, false);
        }

        [Test]
        public void GetToolType_WhenSimulateKeyboardComesFromFirstPartyToolsAssembly_ReturnsBundledPluginType()
        {
            // Tests that simulate-keyboard is a bundled plugin instead of an application-layer tool.
            UnityCliLoopToolRegistry registry = ToolRegistryTestFactory.Create();

            System.Type toolType = registry.GetToolType("simulate-keyboard");

            Assert.That(toolType, Is.Not.Null);
            Assert.That(toolType.Assembly.GetName().Name, Is.EqualTo(SimulateKeyboardAssemblyName));
            AssertThirdPartyTool(toolType, false);
        }

        [Test]
        public void GetToolType_WhenSimulateMouseInputComesFromFirstPartyToolsAssembly_ReturnsBundledPluginType()
        {
            // Tests that simulate-mouse-input is a bundled plugin instead of an application-layer tool.
            UnityCliLoopToolRegistry registry = ToolRegistryTestFactory.Create();

            System.Type toolType = registry.GetToolType("simulate-mouse-input");

            Assert.That(toolType, Is.Not.Null);
            Assert.That(toolType.Assembly.GetName().Name, Is.EqualTo(SimulateMouseInputAssemblyName));
            AssertThirdPartyTool(toolType, false);
        }

        [Test]
        public void GetToolType_WhenSimulateMouseUiComesFromFirstPartyToolsAssembly_ReturnsBundledPluginType()
        {
            // Tests that simulate-mouse-ui is a bundled plugin instead of an application-layer tool.
            UnityCliLoopToolRegistry registry = ToolRegistryTestFactory.Create();

            System.Type toolType = registry.GetToolType("simulate-mouse-ui");

            Assert.That(toolType, Is.Not.Null);
            Assert.That(toolType.Assembly.GetName().Name, Is.EqualTo(SimulateMouseUiAssemblyName));
            AssertThirdPartyTool(toolType, false);
        }

        [Test]
        public void GetToolType_WhenPausePointComesFromFirstPartyToolsAssembly_ReturnsBundledPluginType()
        {
            // Tests that pause point tools are bundled plugins instead of application-layer tools.
            UnityCliLoopToolRegistry registry = ToolRegistryTestFactory.Create();

            System.Type enableToolType = registry.GetToolType(UnityCliLoopConstants.TOOL_NAME_ENABLE_PAUSE_POINT);
            System.Type clearToolType = registry.GetToolType(UnityCliLoopConstants.TOOL_NAME_CLEAR_PAUSE_POINT);

            Assert.That(enableToolType, Is.Not.Null);
            Assert.That(clearToolType, Is.Not.Null);
            Assert.That(enableToolType.Assembly.GetName().Name, Is.EqualTo(PausePointAssemblyName));
            Assert.That(clearToolType.Assembly.GetName().Name, Is.EqualTo(PausePointAssemblyName));
            AssertThirdPartyTool(enableToolType, false);
            AssertThirdPartyTool(clearToolType, false);
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
                new JObject(),
                CancellationToken.None);

            GetVersionResponse getVersionResponse = response as GetVersionResponse;
            Assert.That(getVersionResponse, Is.Not.Null);
            Assert.That(getVersionResponse.UnityVersion, Is.Not.Empty);

            JObject serializedResponse = JObject.FromObject(response);
            Assert.That(serializedResponse["Ver"], Is.Null);
            Assert.That(serializedResponse["Platform"], Is.Null);
            Assert.That(serializedResponse["DataPath"], Is.Null);
            Assert.That(serializedResponse["PersistentDataPath"], Is.Null);
            Assert.That(serializedResponse["TemporaryCachePath"], Is.Null);
            Assert.That(serializedResponse["IsEditor"], Is.Null);
            Assert.That(serializedResponse["ProductName"], Is.Null);
            Assert.That(serializedResponse["CompanyName"], Is.Null);
        }

        [Test]
        public async Task ExecuteCommandAsync_WhenCommandIsGetToolDetails_ReturnsCatalogWithoutInternalCommands()
        {
            // Tests that CLI catalog access still works without registering the catalog command as a tool.
            UnityCliLoopToolResponse response = await UnityApiHandler.ExecuteCommandAsync(
                UnityCliLoopConstants.COMMAND_NAME_GET_TOOL_DETAILS,
                new JObject(),
                CancellationToken.None);

            GetToolDetailsResponse getToolDetailsResponse = response as GetToolDetailsResponse;
            Assert.That(getToolDetailsResponse, Is.Not.Null);

            JObject serializedResponse = JObject.FromObject(response);
            Assert.That(serializedResponse["Ver"], Is.Null);

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
        public async Task ExecuteCommandAsync_WhenCommandIsGetCompileStatus_ReturnsBridgeStatusPayload()
        {
            // Tests that compile status polling routes as a CLI-only bridge command, not a public tool.
            UnityCliLoopToolResponse response = await UnityApiHandler.ExecuteCommandAsync(
                UnityCliLoopConstants.COMMAND_NAME_GET_COMPILE_STATUS,
                new JObject(),
                CancellationToken.None);

            GetCompileStatusResponse getCompileStatusResponse = response as GetCompileStatusResponse;
            Assert.That(getCompileStatusResponse, Is.Not.Null);
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
            AssertThirdPartyTool(registry.GetToolType("hello-world"), true);
        }

        [Test]
        public async Task ExecuteToolAsync_WhenSampleToolUsesTypedContract_ReturnsTypedResponse()
        {
            // Tests that third-party sample tools deserialize camelCase JSON into typed schema values.
            UnityCliLoopToolRegistry registry = ToolRegistryTestFactory.Create();
            UnityCliLoopToolExecutionService executionService = new(new NoOpEditorRuntimeStatePort());
            JObject parameters = JObject.FromObject(new
            {
                name = "<USER_NAME>",
                language = "french",
                includeTimestamp = false
            });

            UnityCliLoopToolResponse response = await executionService.ExecuteToolAsync(
                registry,
                "hello-world",
                parameters,
                CancellationToken.None);
            JObject serializedResponse = JObject.FromObject(response);

            Assert.That(serializedResponse.Value<string>("Message"), Is.EqualTo("Bonjour, <USER_NAME>!"));
            Assert.That(serializedResponse.Value<string>("Language"), Is.EqualTo("french"));
            Assert.That(serializedResponse["Timestamp"]?.Type, Is.EqualTo(JTokenType.Null));
        }

        [Test]
        public async Task ExecuteToolAsync_WhenParamsAreOmitted_UsesDefaultSchema()
        {
            // Tests that tool execution may omit params and still use schema defaults.
            UnityCliLoopToolRegistry registry = ToolRegistryTestFactory.Create();
            UnityCliLoopToolExecutionService executionService = new(new NoOpEditorRuntimeStatePort());

            UnityCliLoopToolResponse response = await executionService.ExecuteToolAsync(
                registry,
                "hello-world",
                null,
                CancellationToken.None);
            JObject serializedResponse = JObject.FromObject(response);

            Assert.That(serializedResponse.Value<string>("Message"), Is.EqualTo("Hello, World!"));
            Assert.That(serializedResponse.Value<string>("Language"), Is.EqualTo("english"));
        }

        [Test]
        public async Task ExecuteToolAsync_WhenToolReturnsResponse_DoesNotAddProtocolVersionToResponseInstance()
        {
            // Tests that tool responses do not carry obsolete per-response protocol version metadata.
            UnityCliLoopToolRegistry registry = ToolRegistryTestFactory.Create();
            UnityCliLoopToolExecutionService executionService = new(new NoOpEditorRuntimeStatePort());
            JObject parameters = JObject.FromObject(new
            {
                name = "Masamichi",
                language = "english",
                includeTimestamp = false
            });

            UnityCliLoopToolResponse response = await executionService.ExecuteToolAsync(
                registry,
                "hello-world",
                parameters,
                CancellationToken.None);
            JObject serializedResponse = JObject.FromObject(response);

            Assert.That(serializedResponse["Ver"], Is.Null);
        }

        [Test]
        public void StaticRegistrarCustomToolMethods_RegisterAndUnregisterManualTool()
        {
            // Tests that extension-facing static registrar APIs still delegate to the shared registry.
            UnityCliLoopToolRegistrarService previousService = ApplicationRegistrar.Service;
            IToolSettingsPort toolSettingsPort = new ToolSettingsRepository();
            UnityCliLoopToolRegistrarService service = new(
                new EmptyInternalToolNameProvider(),
                toolSettingsPort,
                new UnityCliLoopToolExecutionService(new NoOpEditorRuntimeStatePort()),
                UnityCliLoopToolDiscovery.DiscoverTools);
            ManualRegistrationTool tool = new();

            ApplicationRegistrar.RegisterService(service);
            try
            {
                ApplicationRegistrar.RegisterCustomTool(tool);

                string[] toolNames = ApplicationRegistrar.GetRegisteredCustomTools()
                    .Select(toolInfo => toolInfo.Name)
                    .ToArray();
                Assert.That(ApplicationRegistrar.IsCustomToolRegistered(tool.ToolName), Is.True);
                Assert.That(toolNames, Does.Contain(tool.ToolName));

                ApplicationRegistrar.UnregisterCustomTool(tool.ToolName);

                Assert.That(ApplicationRegistrar.IsCustomToolRegistered(tool.ToolName), Is.False);
            }
            finally
            {
                ApplicationRegistrar.RegisterService(previousService);
            }
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
            string[] references = ReadResolvedReferences(asmdefPath);

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
            string[] references = ReadResolvedReferences(asmdefPath);

            Assert.That(references, Does.Contain(ClearConsoleAssemblyName));
            Assert.That(references, Does.Contain(CompileAssemblyName));
            Assert.That(references, Does.Contain(ControlPlayModeAssemblyName));
            Assert.That(references, Does.Contain(ExecuteDynamicCodeAssemblyName));
            Assert.That(references, Does.Contain(FindGameObjectsAssemblyName));
            Assert.That(references, Does.Contain(GetHierarchyAssemblyName));
            Assert.That(references, Does.Contain(GetLogsAssemblyName));
            Assert.That(references, Does.Contain(PausePointAssemblyName));
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

        private static string[] ReadResolvedReferences(string asmdefPath)
        {
            JObject asmdef = JObject.Parse(File.ReadAllText(asmdefPath));
            string[] references = asmdef["references"]?.Values<string>().ToArray() ?? new string[0];
            return references.Select(ResolveAsmdefReference).ToArray();
        }

        private static void AssertThirdPartyTool(System.Type toolType, bool expected)
        {
            Assert.That(toolType, Is.Not.Null);
            string assemblyName = toolType.Assembly.GetName().Name;
            Assert.That(ToolAssemblyClassifier.IsThirdPartyAssembly(assemblyName), Is.EqualTo(expected));
        }

        private static string ResolveAsmdefReference(string reference)
        {
            const string guidPrefix = "GUID:";
            if (!reference.StartsWith(guidPrefix, System.StringComparison.Ordinal))
            {
                return reference;
            }

            string guid = reference.Substring(guidPrefix.Length);
            string projectRoot = UnityCliLoopPathResolver.GetProjectRoot();
            foreach (string metaPath in Directory.GetFiles(projectRoot, "*.asmdef.meta", SearchOption.AllDirectories))
            {
                string meta = File.ReadAllText(metaPath);
                if (!meta.Contains($"guid: {guid}"))
                {
                    continue;
                }

                string resolvedAsmdefPath = metaPath.Substring(0, metaPath.Length - ".meta".Length);
                JObject asmdef = JObject.Parse(File.ReadAllText(resolvedAsmdefPath));
                return asmdef["name"]?.Value<string>() ?? reference;
            }

            return reference;
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

        private sealed class ManualRegistrationTool : IUnityCliLoopTool
        {
            public string ToolName => "manual-registration-test";
            public ToolParameterSchema ParameterSchema { get; } = new();

            public Task<UnityCliLoopToolResponse> ExecuteAsync(JToken paramsToken, CancellationToken ct)
            {
                UnityCliLoopToolResponse response = new ManualRegistrationResponse();
                return Task.FromResult(response);
            }
        }

        private sealed class ManualRegistrationResponse : UnityCliLoopToolResponse
        {
        }
    }
}
