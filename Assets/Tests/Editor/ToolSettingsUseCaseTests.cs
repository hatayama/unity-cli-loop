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
            ToolSettingsUseCase useCase = new(toolSettingsService, toolRegistrarService);
            useCase.WarmupRegistry();

            bool isAvailable = useCase.TryGetToolCatalog(out ToolSettingsUseCase.ToolCatalogItem[] allTools);
            string[] toolNames = allTools.Select(tool => tool.Name).ToArray();

            Assert.That(isAvailable, Is.True);
            Assert.That(toolNames, Does.Contain(UnityCliLoopConstants.COMMAND_NAME_WAIT_FOR_PAUSE_POINT));
            Assert.That(toolNames, Does.Contain(UnityCliLoopConstants.COMMAND_NAME_PAUSE_POINT_STATUS));
        }
    }
}
