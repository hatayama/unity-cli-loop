using System;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies the construction contract of a Methods[] row a hot reload did not apply.
    /// </summary>
    public sealed class HotReloadUnappliedRowTests
    {
        /// <summary>
        /// Verifies a row without a label is rejected, naming the label parameter.
        /// </summary>
        [TestCase(null)]
        [TestCase("")]
        public void Constructor_WhenLabelMissing_ThrowsArgumentException(string label)
        {
            ArgumentException exception = Assert.Throws<ArgumentException>(
                () => new HotReloadUnappliedRow(label, HotReloadUnappliedRowKind.Skipped));

            Assert.That(exception.ParamName, Is.EqualTo("label"));
        }

        /// <summary>
        /// Verifies a row with a label keeps the label and kind it was given.
        /// </summary>
        [Test]
        public void Constructor_WhenLabelPresent_KeepsLabelAndKind()
        {
            HotReloadUnappliedRow row = new HotReloadUnappliedRow("(file)", HotReloadUnappliedRowKind.Failed);

            Assert.That(row.Label, Is.EqualTo("(file)"));
            Assert.That(row.Kind, Is.EqualTo(HotReloadUnappliedRowKind.Failed));
        }
    }
}
