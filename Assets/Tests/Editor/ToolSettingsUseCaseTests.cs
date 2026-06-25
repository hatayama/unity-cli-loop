using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Application;
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
        public void TryGetToolCatalog_WhenRegistryAvailable_IncludesNativePausePointCommands()
        {
            // Verifies CLI-native pause point commands are user-toggleable built-in tools.
            ToolSettingsService toolSettingsService = new(new ToolSettingsRepository());
            UnityCliLoopToolRegistrarService toolRegistrarService = new(
                new EmptyInternalToolNameProvider(),
                toolSettingsService,
                new UnityCliLoopToolExecutionService());
            ToolSettingsUseCase useCase = new(
                toolSettingsService,
                toolRegistrarService,
                new StaticToolSkillDescriptionProvider(new Dictionary<string, string>()));
            useCase.WarmupRegistry();

            bool isAvailable = useCase.TryGetToolCatalog(out ToolSettingsUseCase.ToolCatalogItem[] allTools);
            string[] toolNames = allTools.Select(tool => tool.Name).ToArray();

            Assert.That(isAvailable, Is.True);
            Assert.That(toolNames, Does.Contain(UnityCliLoopConstants.COMMAND_NAME_WAIT_FOR_PAUSE_POINT));
            Assert.That(toolNames, Does.Contain(UnityCliLoopConstants.COMMAND_NAME_PAUSE_POINT_STATUS));
        }

        [Test]
        public void TryGetToolCatalog_WhenSkillDescriptionExists_IncludesDescription()
        {
            // Verifies Tool Settings can show source skill descriptions without changing public tool metadata.
            ToolSettingsService toolSettingsService = new(new ToolSettingsRepository());
            UnityCliLoopToolRegistrarService toolRegistrarService = new(
                new EmptyInternalToolNameProvider(),
                toolSettingsService,
                new UnityCliLoopToolExecutionService());
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
    }
}
