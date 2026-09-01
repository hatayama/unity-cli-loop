using System;
using System.IO;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies default and explicit video output path resolution.
    /// </summary>
    public sealed class RecordVideoOutputPathResolverTests
    {
        /// <summary>
        /// What: an empty path uses the default Videos directory and timestamped mp4 name.
        /// </summary>
        [Test]
        public void Resolve_WhenPathIsEmpty_ReturnsDefaultMp4Name()
        {
            DateTime now = new DateTime(2026, 9, 1, 15, 4, 5, DateTimeKind.Utc);

            string resolved = RecordVideoOutputPathResolver.Resolve("", "/project", now, false);

            string expected = Path.Combine(
                "/project",
                UnityCliLoopConstants.OUTPUT_ROOT_DIR,
                UnityCliLoopConstants.VIDEOS_DIR,
                "gameview_20260901_150405.mp4");
            Assert.That(resolved, Is.EqualTo(expected));
        }

        /// <summary>
        /// What: an explicit path is returned as a full path without creating directories.
        /// </summary>
        [Test]
        public void Resolve_WhenPathIsSpecified_ReturnsFullPath()
        {
            string requested = Path.Combine("custom", "clip.mp4");

            string resolved = RecordVideoOutputPathResolver.Resolve(requested, "/project", DateTime.UtcNow, false);

            Assert.That(resolved, Is.EqualTo(Path.GetFullPath(requested)));
        }

        /// <summary>
        /// What: Linux default output uses .webm because H.264 is unavailable.
        /// </summary>
        [Test]
        public void Resolve_WhenLinuxAndPathIsEmpty_ReturnsDefaultWebmName()
        {
            DateTime now = new DateTime(2026, 9, 1, 15, 4, 5, DateTimeKind.Utc);

            string resolved = RecordVideoOutputPathResolver.Resolve("", "/project", now, true);

            Assert.That(resolved, Does.EndWith(".webm"));
            Assert.That(resolved, Does.Contain("gameview_20260901_150405.webm"));
        }
    }
}
