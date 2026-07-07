using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    public sealed class ToolExecutionSessionTests
    {
        [Test]
        public void TryEnter_WhenNoToolIsRunning_ShouldEnterAndAllowExitThenNextTool()
        {
            // Tests that an empty session admits a tool and releases the slot after exit.
            ToolExecutionSession session = new ToolExecutionSession();

            ToolExecutionSessionEnterResult firstResult = session.TryEnter("first-tool");
            session.Exit();
            ToolExecutionSessionEnterResult secondResult = session.TryEnter("second-tool");

            Assert.That(firstResult.IsEntered, Is.True);
            Assert.That(firstResult.RunningToolName, Is.Empty);
            Assert.That(secondResult.IsEntered, Is.True);

            session.Exit();
        }

        [Test]
        public void TryEnter_WhenDifferentToolIsRunning_ShouldReturnBusyWithRunningToolName()
        {
            // Tests that the single-flight gate reports the already running tool for rejected requests.
            ToolExecutionSession session = new ToolExecutionSession();

            ToolExecutionSessionEnterResult firstResult = session.TryEnter("running-tool");
            ToolExecutionSessionEnterResult busyResult = session.TryEnter("requested-tool");

            Assert.That(firstResult.IsEntered, Is.True);
            Assert.That(busyResult.IsEntered, Is.False);
            Assert.That(busyResult.RunningToolName, Is.EqualTo("running-tool"));

            session.Exit();
        }

        [Test]
        public void TryEnter_WhenExecuteDynamicCodeIsAlreadyRunning_ShouldAllowSecondExecuteDynamicCode()
        {
            // Tests that execute-dynamic-code keeps its existing shared execution slot behavior.
            ToolExecutionSession session = new ToolExecutionSession();

            ToolExecutionSessionEnterResult firstResult =
                session.TryEnter(UnityCliLoopConstants.TOOL_NAME_EXECUTE_DYNAMIC_CODE);
            ToolExecutionSessionEnterResult secondResult =
                session.TryEnter(UnityCliLoopConstants.TOOL_NAME_EXECUTE_DYNAMIC_CODE);

            Assert.That(firstResult.IsEntered, Is.True);
            Assert.That(secondResult.IsEntered, Is.True);

            session.Exit();
            session.Exit();
        }

        [Test]
        public void Exit_WhenTwoSharedDynamicCodeExecutionsEntered_ShouldKeepSlotBusyUntilBothExit()
        {
            // Tests that shared execute-dynamic-code entries keep the session busy until every entry exits.
            ToolExecutionSession session = new ToolExecutionSession();

            session.TryEnter(UnityCliLoopConstants.TOOL_NAME_EXECUTE_DYNAMIC_CODE);
            session.TryEnter(UnityCliLoopConstants.TOOL_NAME_EXECUTE_DYNAMIC_CODE);

            ToolExecutionSessionEnterResult busyBeforeExit = session.TryEnter("other-tool");
            session.Exit();
            ToolExecutionSessionEnterResult busyAfterOneExit = session.TryEnter("other-tool");
            session.Exit();
            ToolExecutionSessionEnterResult enteredAfterBothExit = session.TryEnter("other-tool");

            Assert.That(busyBeforeExit.IsEntered, Is.False);
            Assert.That(busyBeforeExit.RunningToolName, Is.EqualTo(UnityCliLoopConstants.TOOL_NAME_EXECUTE_DYNAMIC_CODE));
            Assert.That(busyAfterOneExit.IsEntered, Is.False);
            Assert.That(busyAfterOneExit.RunningToolName, Is.EqualTo(UnityCliLoopConstants.TOOL_NAME_EXECUTE_DYNAMIC_CODE));
            Assert.That(enteredAfterBothExit.IsEntered, Is.True);

            session.Exit();
        }
    }
}
