using System;
using System.Threading;
using NUnit.Framework;
using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.UnitTests
{
    [TestFixture]
    public class CommandRunnerExecutionSlotTests
    {
        [Test]
        public void TryBegin_WhenUndoBeginThrows_ShouldNotLeaveSlotRunning()
        {
            // Verifies Undo failure before marking running does not permanently stick the slot.
            CommandRunnerExecutionSlot slot = new();

            Assert.That(
                () => slot.TryBegin(
                    () => throw new InvalidOperationException("undo begin failed"),
                    out int _,
                    out CancellationTokenSource _),
                Throws.TypeOf<InvalidOperationException>());

            Assert.That(slot.IsRunning, Is.False);
            Assert.That(
                slot.TryBegin(() => 7, out int undoGroup, out CancellationTokenSource cts),
                Is.True);
            Assert.That(undoGroup, Is.EqualTo(7));
            Assert.That(cts, Is.Not.Null);
            slot.End(undoGroup, _ => { });
            Assert.That(slot.IsRunning, Is.False);
        }

        [Test]
        public void End_WhenUndoCollapseThrows_ShouldClearRunningFlag()
        {
            // Verifies collapse failures still clear running so later begin can succeed.
            CommandRunnerExecutionSlot slot = new();
            Assert.That(slot.TryBegin(() => 3, out int undoGroup, out CancellationTokenSource _), Is.True);

            Assert.That(
                () => slot.End(undoGroup, _ => throw new InvalidOperationException("undo collapse failed")),
                Throws.TypeOf<InvalidOperationException>());

            Assert.That(slot.IsRunning, Is.False);
            Assert.That(slot.TryBegin(() => 4, out int _, out CancellationTokenSource _), Is.True);
            slot.End(4, _ => { });
        }

        [Test]
        public void TryBegin_WhenAlreadyRunning_ShouldReturnFalseWithoutCallingUndo()
        {
            // Verifies busy rejection happens before undo begin side effects.
            CommandRunnerExecutionSlot slot = new();
            int undoBeginCalls = 0;
            Assert.That(
                slot.TryBegin(
                    () =>
                    {
                        undoBeginCalls++;
                        return 1;
                    },
                    out int _,
                    out CancellationTokenSource _),
                Is.True);

            Assert.That(
                slot.TryBegin(
                    () =>
                    {
                        undoBeginCalls++;
                        return 2;
                    },
                    out int _,
                    out CancellationTokenSource _),
                Is.False);
            Assert.That(undoBeginCalls, Is.EqualTo(1));
            slot.End(1, _ => { });
        }
    }
}
