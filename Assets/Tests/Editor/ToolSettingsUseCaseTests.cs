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
        public void TryGetToolCatalog_WhenRegistryAvailable_ShowsOnlyWaitForPausePointCommand()
        {
            // Verifies Tool Settings exposes only the parent pause point command.
            ToolSettingsService toolSettingsService = new(new InMemoryToolSettingsPort());
            UnityCliLoopToolRegistrarService toolRegistrarService = new(
                new EmptyInternalToolNameProvider(),
                toolSettingsService,
                new UnityCliLoopToolExecutionService(),
                UnityCliLoopToolDiscovery.DiscoverTools);
            ToolSettingsUseCase useCase = new(
                toolSettingsService,
                toolRegistrarService,
                new StaticToolSkillDescriptionProvider(new Dictionary<string, string>()));
            useCase.WarmupRegistry();

            bool isAvailable = useCase.TryGetToolCatalog(out ToolSettingsUseCase.ToolCatalogItem[] allTools);
            string[] toolNames = allTools.Select(tool => tool.Name).ToArray();

            Assert.That(isAvailable, Is.True);
            Assert.That(toolNames, Does.Contain(UnityCliLoopConstants.COMMAND_NAME_WAIT_FOR_PAUSE_POINT));
            Assert.That(toolNames, Does.Not.Contain(UnityCliLoopConstants.TOOL_NAME_ENABLE_PAUSE_POINT));
            Assert.That(toolNames, Does.Not.Contain(UnityCliLoopConstants.TOOL_NAME_CLEAR_PAUSE_POINT));
            Assert.That(toolNames, Does.Not.Contain(UnityCliLoopConstants.COMMAND_NAME_PAUSE_POINT_STATUS));
        }

        [Test]
        public void IsToolEnabled_WhenWaitForPausePointDisabled_DisablesPausePointAuxiliaryTools()
        {
            // Verifies pause point auxiliary tools follow the wait-for-pause-point setting.
            ToolSettingsService toolSettingsService = new(new InMemoryToolSettingsPort());
            UnityCliLoopToolRegistrarService toolRegistrarService = new(
                new EmptyInternalToolNameProvider(),
                toolSettingsService,
                new UnityCliLoopToolExecutionService(),
                UnityCliLoopToolDiscovery.DiscoverTools);
            ToolSettingsUseCase useCase = new(
                toolSettingsService,
                toolRegistrarService,
                new StaticToolSkillDescriptionProvider(new Dictionary<string, string>()));

            useCase.SetToolEnabled(UnityCliLoopConstants.COMMAND_NAME_WAIT_FOR_PAUSE_POINT, false);

            Assert.That(useCase.IsToolEnabled(UnityCliLoopConstants.COMMAND_NAME_WAIT_FOR_PAUSE_POINT), Is.False);
            Assert.That(useCase.IsToolEnabled(UnityCliLoopConstants.TOOL_NAME_ENABLE_PAUSE_POINT), Is.False);
            Assert.That(useCase.IsToolEnabled(UnityCliLoopConstants.TOOL_NAME_CLEAR_PAUSE_POINT), Is.False);
            Assert.That(useCase.IsToolEnabled(UnityCliLoopConstants.COMMAND_NAME_PAUSE_POINT_STATUS), Is.False);
        }

        [Test]
        public void TryGetToolCatalog_WhenSkillDescriptionExists_IncludesDescription()
        {
            // Verifies Tool Settings can show source skill descriptions without changing public tool metadata.
            ToolSettingsService toolSettingsService = new(new InMemoryToolSettingsPort());
            UnityCliLoopToolRegistrarService toolRegistrarService = new(
                new EmptyInternalToolNameProvider(),
                toolSettingsService,
                new UnityCliLoopToolExecutionService(),
                UnityCliLoopToolDiscovery.DiscoverTools);
            Dictionary<string, string> descriptions = new()
            {
                [UnityCliLoopConstants.COMMAND_NAME_WAIT_FOR_PAUSE_POINT] = "Pause point description"
            };
            ToolSettingsUseCase useCase = new(
                toolSettingsService,
                toolRegistrarService,
                new StaticToolSkillDescriptionProvider(descriptions));
            useCase.WarmupRegistry();

            bool isAvailable = useCase.TryGetToolCatalog(out ToolSettingsUseCase.ToolCatalogItem[] allTools);
            ToolSettingsUseCase.ToolCatalogItem waitTool = allTools
                .Single(tool => tool.Name == UnityCliLoopConstants.COMMAND_NAME_WAIT_FOR_PAUSE_POINT);

            Assert.That(isAvailable, Is.True);
            Assert.That(waitTool.SkillDescription, Is.EqualTo("Pause point description"));
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
