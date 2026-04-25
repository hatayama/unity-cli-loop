using NUnit.Framework;

namespace io.github.hatayama.uLoopMCP
{
    public class McpEditorWindowRefreshPolicyTests
    {
        [Test]
        public void ShouldRefreshOnEditorUpdate_WhenRepaintIsRequested_ReturnsTrue()
        {
            RuntimeState runtimeState = new RuntimeState(needsRepaint: true);

            bool shouldRefresh = McpEditorWindowRefreshPolicy.ShouldRefreshOnEditorUpdate(runtimeState);

            Assert.That(shouldRefresh, Is.True);
        }

        [Test]
        public void ShouldRefreshOnEditorUpdate_WhenPostCompileModeHasNoRepaintRequest_ReturnsFalse()
        {
            RuntimeState runtimeState = new RuntimeState(
                needsRepaint: false,
                isPostCompileMode: true);

            bool shouldRefresh = McpEditorWindowRefreshPolicy.ShouldRefreshOnEditorUpdate(runtimeState);

            Assert.That(shouldRefresh, Is.False);
        }
    }
}
