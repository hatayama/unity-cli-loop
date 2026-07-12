using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.CompositionRoot;
using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.Infrastructure;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies tool settings use case catalog behavior.
    /// </summary>
    [TestFixture]
    public class ToolSettingsUseCaseTests
    {
        [Test]
        public void TryGetToolCatalog_WhenRegistryAvailable_ShowsOnlyPausePointSettingsTool()
        {
            // Verifies Tool Settings exposes only the pause-point family toggle.
            IToolSettingsPort toolSettingsPort = new InMemoryToolSettingsPort();
            UnityCliLoopToolRegistrarService toolRegistrarService = new(
                new EmptyInternalToolNameProvider(),
                toolSettingsPort,
                new UnityCliLoopToolExecutionService(new NoOpEditorRuntimeStatePort()),
                UnityCliLoopToolDiscovery.DiscoverTools);
            ToolSettingsUseCase useCase = new(
                toolSettingsPort,
                toolRegistrarService,
                new StaticToolSkillDescriptionProvider(new Dictionary<string, string>()));
            useCase.WarmupRegistry();

            bool isAvailable = useCase.TryGetToolCatalog(out ToolSettingsUseCase.ToolCatalogItem[] allTools);
            string[] toolNames = allTools.Select(tool => tool.Name).ToArray();

            Assert.That(isAvailable, Is.True);
            Assert.That(toolNames, Does.Contain(UnityCliLoopConstants.SETTINGS_TOOL_NAME_PAUSE_POINT));
            Assert.That(toolNames, Does.Not.Contain(UnityCliLoopConstants.COMMAND_NAME_AWAIT_PAUSE_POINT));
            Assert.That(toolNames, Does.Not.Contain(UnityCliLoopConstants.TOOL_NAME_ENABLE_PAUSE_POINT));
            Assert.That(toolNames, Does.Not.Contain(UnityCliLoopConstants.TOOL_NAME_CLEAR_PAUSE_POINT));
            Assert.That(toolNames, Does.Not.Contain(UnityCliLoopConstants.COMMAND_NAME_PAUSE_POINT_STATUS));
        }

        [Test]
        public void IsToolEnabled_WhenPausePointDisabled_DisablesPausePointAuxiliaryTools()
        {
            // Verifies pause point auxiliary tools follow the pause-point settings toggle.
            IToolSettingsPort toolSettingsPort = new InMemoryToolSettingsPort();
            UnityCliLoopToolRegistrarService toolRegistrarService = new(
                new EmptyInternalToolNameProvider(),
                toolSettingsPort,
                new UnityCliLoopToolExecutionService(new NoOpEditorRuntimeStatePort()),
                UnityCliLoopToolDiscovery.DiscoverTools);
            ToolSettingsUseCase useCase = new(
                toolSettingsPort,
                toolRegistrarService,
                new StaticToolSkillDescriptionProvider(new Dictionary<string, string>()));

            useCase.SetToolEnabled(UnityCliLoopConstants.SETTINGS_TOOL_NAME_PAUSE_POINT, false);

            Assert.That(useCase.IsToolEnabled(UnityCliLoopConstants.SETTINGS_TOOL_NAME_PAUSE_POINT), Is.False);
            Assert.That(useCase.IsToolEnabled(UnityCliLoopConstants.COMMAND_NAME_AWAIT_PAUSE_POINT), Is.False);
            Assert.That(useCase.IsToolEnabled(UnityCliLoopConstants.TOOL_NAME_ENABLE_PAUSE_POINT), Is.False);
            Assert.That(useCase.IsToolEnabled(UnityCliLoopConstants.TOOL_NAME_CLEAR_PAUSE_POINT), Is.False);
            Assert.That(useCase.IsToolEnabled(UnityCliLoopConstants.COMMAND_NAME_PAUSE_POINT_STATUS), Is.False);
        }

        [Test]
        public void IsToolEnabled_WhenAwaitPausePointDisabled_DisablesPausePointAuxiliaryTools()
        {
            // Verifies disabling via the await-pause-point command name still maps to the pause-point toggle.
            IToolSettingsPort toolSettingsPort = new InMemoryToolSettingsPort();
            UnityCliLoopToolRegistrarService toolRegistrarService = new(
                new EmptyInternalToolNameProvider(),
                toolSettingsPort,
                new UnityCliLoopToolExecutionService(new NoOpEditorRuntimeStatePort()),
                UnityCliLoopToolDiscovery.DiscoverTools);
            ToolSettingsUseCase useCase = new(
                toolSettingsPort,
                toolRegistrarService,
                new StaticToolSkillDescriptionProvider(new Dictionary<string, string>()));

            useCase.SetToolEnabled(UnityCliLoopConstants.COMMAND_NAME_AWAIT_PAUSE_POINT, false);

            Assert.That(useCase.IsToolEnabled(UnityCliLoopConstants.COMMAND_NAME_AWAIT_PAUSE_POINT), Is.False);
            Assert.That(useCase.IsToolEnabled(UnityCliLoopConstants.COMMAND_NAME_PAUSE_POINT_STATUS), Is.False);
        }

        [Test]
        public void ToolSettingsToolLinkPolicy_WhenAwaitPausePointRequested_IsNotUserFacingAndMapsToPausePoint()
        {
            // Verifies await-pause-point is auxiliary and resolves to the pause-point settings key.
            Assert.That(
                ToolSettingsToolLinkPolicy.IsUserFacingToolSettingsTool(
                    UnityCliLoopConstants.COMMAND_NAME_AWAIT_PAUSE_POINT),
                Is.False);
            Assert.That(
                ToolSettingsToolLinkPolicy.GetSettingsToolName(
                    UnityCliLoopConstants.COMMAND_NAME_AWAIT_PAUSE_POINT),
                Is.EqualTo(UnityCliLoopConstants.SETTINGS_TOOL_NAME_PAUSE_POINT));
        }

        [Test]
        public void TryGetToolCatalog_WhenSkillDescriptionExists_IncludesDescription()
        {
            // Verifies Tool Settings can show source skill descriptions without changing public tool metadata.
            IToolSettingsPort toolSettingsPort = new InMemoryToolSettingsPort();
            UnityCliLoopToolRegistrarService toolRegistrarService = new(
                new EmptyInternalToolNameProvider(),
                toolSettingsPort,
                new UnityCliLoopToolExecutionService(new NoOpEditorRuntimeStatePort()),
                UnityCliLoopToolDiscovery.DiscoverTools);
            Dictionary<string, string> descriptions = new()
            {
                [UnityCliLoopConstants.SETTINGS_TOOL_NAME_PAUSE_POINT] = "Pause point description"
            };
            ToolSettingsUseCase useCase = new(
                toolSettingsPort,
                toolRegistrarService,
                new StaticToolSkillDescriptionProvider(descriptions));
            useCase.WarmupRegistry();

            bool isAvailable = useCase.TryGetToolCatalog(out ToolSettingsUseCase.ToolCatalogItem[] allTools);
            ToolSettingsUseCase.ToolCatalogItem pausePointTool = allTools
                .Single(tool => tool.Name == UnityCliLoopConstants.SETTINGS_TOOL_NAME_PAUSE_POINT);

            Assert.That(isAvailable, Is.True);
            Assert.That(pausePointTool.SkillDescription, Is.EqualTo("Pause point description"));
        }

        [Test]
        public void IsSkillDisabledByToolSettings_WhenPausePointDisabled_ReturnsTrueForPausePointSkill()
        {
            // Verifies disabledTools pause-point key matches the pause-point skill tool name.
            SkillInstallLayout.SkillSourceInfo skill = new(
                "uloop-pause-point",
                UnityCliLoopConstants.SETTINGS_TOOL_NAME_PAUSE_POINT,
                new Dictionary<string, byte[]>());
            string[] disabledTools = { UnityCliLoopConstants.SETTINGS_TOOL_NAME_PAUSE_POINT };

            bool isDisabled = SkillDisabledToolFilter.IsSkillDisabledByToolSettings(
                skill,
                disabledTools);

            Assert.That(isDisabled, Is.True);
        }

        private sealed class StaticToolSkillDescriptionProvider : IToolSkillDescriptionProvider
        {
            private readonly IReadOnlyDictionary<string, string> _descriptions;

            public StaticToolSkillDescriptionProvider(IReadOnlyDictionary<string, string> descriptions)
            {
                _descriptions = descriptions;
            }

            public IReadOnlyDictionary<string, string> GetSkillDescriptionsByToolName()
            {
                return _descriptions;
            }
        }

        private sealed class InMemoryToolSettingsPort : IToolSettingsPort
        {
            private readonly HashSet<string> _disabledTools = new();

            public bool IsToolEnabled(string toolName)
            {
                return !_disabledTools.Contains(toolName);
            }

            public void SetToolEnabled(string toolName, bool enabled)
            {
                if (enabled)
                {
                    _disabledTools.Remove(toolName);
                    return;
                }

                _disabledTools.Add(toolName);
            }

            public string[] GetDisabledTools()
            {
                return _disabledTools.ToArray();
            }

            public void InvalidateCache()
            {
            }
        }
    }
}
