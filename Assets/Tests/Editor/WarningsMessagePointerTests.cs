using System;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies the Message suffix that points callers at a response's Warnings array.
    /// </summary>
    public sealed class WarningsMessagePointerTests
    {
        /// <summary>
        /// Verifies a message is returned unchanged when there are no warnings to point at.
        /// </summary>
        [TestCase(0)]
        [TestCase(-1)]
        public void Append_WhenNoWarnings_ReturnsMessageUnchanged(int warningCount)
        {
            string result = WarningsMessagePointer.Append("Captured 1 window.", warningCount);

            Assert.That(result, Is.EqualTo("Captured 1 window."));
        }
    }
}
