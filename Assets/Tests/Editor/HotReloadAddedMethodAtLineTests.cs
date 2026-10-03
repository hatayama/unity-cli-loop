using System;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies the construction contract of an added method found at a source line.
    /// </summary>
    public sealed class HotReloadAddedMethodAtLineTests
    {
        /// <summary>
        /// Verifies each of the three required name parts is checked on its own.
        /// </summary>
        [TestCase(null, "Tick", "Player")]
        [TestCase("", "Tick", "Player")]
        [TestCase("Game.Player.Tick()", null, "Player")]
        [TestCase("Game.Player.Tick()", "", "Player")]
        [TestCase("Game.Player.Tick()", "Tick", null)]
        [TestCase("Game.Player.Tick()", "Tick", "")]
        public void Constructor_WhenRequiredNameMissing_ThrowsArgumentException(
            string label,
            string methodName,
            string declaringTypeName)
        {
            ArgumentException exception = Assert.Throws<ArgumentException>(
                () => new HotReloadAddedMethodAtLine(label, methodName, declaringTypeName, null));

            Assert.That(
                exception.Message,
                Does.StartWith("An added method at a line needs its label, method name and declaring type name."));
        }

        /// <summary>
        /// Verifies a method on a non-nested type is accepted with no outer type name.
        /// </summary>
        [Test]
        public void Constructor_WhenNotNested_AcceptsNullOuterTypeName()
        {
            HotReloadAddedMethodAtLine method = new HotReloadAddedMethodAtLine("Game.Player.Tick()", "Tick", "Player", null);

            Assert.That(method.Label, Is.EqualTo("Game.Player.Tick()"));
            Assert.That(method.MethodName, Is.EqualTo("Tick"));
            Assert.That(method.DeclaringTypeName, Is.EqualTo("Player"));
            Assert.That(method.NestedOuterTypeName, Is.Null);
        }
    }
}
