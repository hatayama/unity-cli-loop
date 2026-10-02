using System;
using System.Collections.Generic;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies the construction contract of what the latest hot reload says about a file.
    /// </summary>
    public sealed class HotReloadLatestFileReloadTests
    {
        /// <summary>
        /// Verifies a missing row list is rejected rather than stored as null.
        /// </summary>
        [Test]
        public void Constructor_WhenUnappliedRowsNull_ThrowsArgumentNullException()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new HotReloadLatestFileReload(false, null));

            Assert.That(exception.ParamName, Is.EqualTo("unappliedRows"));
        }

        /// <summary>
        /// Verifies the change flag and the given rows are kept as passed.
        /// </summary>
        [Test]
        public void Constructor_WhenRowsGiven_KeepsFlagAndRows()
        {
            List<HotReloadUnappliedRow> rows = new List<HotReloadUnappliedRow>
            {
                new HotReloadUnappliedRow("(unknown)", HotReloadUnappliedRowKind.Skipped)
            };

            HotReloadLatestFileReload reload = new HotReloadLatestFileReload(true, rows);

            Assert.That(reload.FileChangedSince, Is.True);
            Assert.That(reload.UnappliedRows, Is.SameAs(rows));
        }
    }
}
