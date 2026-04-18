using NUnit.Framework;

namespace io.github.hatayama.uLoopMCP
{
    public class McpServerControllerStartupLockTests
    {
        [Test]
        public void CreateOptionalServerStartingLock_WhenLockCreationSucceeds_ShouldReturnOwnershipToken()
        {
            string token = McpServerController.CreateOptionalServerStartingLock(() => "token-123");

            Assert.That(token, Is.EqualTo("token-123"));
        }

        [Test]
        public void CreateOptionalServerStartingLock_WhenLockCreationFails_ShouldContinueWithoutThrowing()
        {
            string token = McpServerController.CreateOptionalServerStartingLock(() => null);

            Assert.That(token, Is.Null);
        }
    }
}
