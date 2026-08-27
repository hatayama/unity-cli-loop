using System;
using System.IO;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// EditMode coverage for hot-reload filesystem path normalization.
    /// </summary>
    public class HotReloadFileSystemPathTests
    {
        /// <summary>
        /// What: a short absolute Windows path remains unchanged.
        /// </summary>
        [Test]
        public void GetFileSystemPath_ShortWindowsPath_ReturnsUnchangedPath()
        {
            if (Path.DirectorySeparatorChar != '\\')
            {
                Assert.Pass("Windows extended-length path handling applies only on Windows.");
                return;
            }

            string path = Path.GetFullPath("short.cs");
            Assert.That(path.Length, Is.LessThan(260), "Test path must remain below legacy MAX_PATH.");

            Assert.That(HotReloadFileSystemPath.GetFileSystemPath(path), Is.EqualTo(path));
        }

        /// <summary>
        /// What: a Windows path at or beyond legacy MAX_PATH receives the extended-length prefix.
        /// </summary>
        [Test]
        public void GetFileSystemPath_LongWindowsPath_AddsExtendedLengthPrefix()
        {
            if (Path.DirectorySeparatorChar != '\\')
            {
                Assert.Pass("Windows extended-length path handling applies only on Windows.");
                return;
            }

            string basePath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "uloop-path-test"));
            string path = basePath + Path.DirectorySeparatorChar +
                new string('a', Math.Max(1, 260 - basePath.Length));
            Assert.That(path.Length, Is.GreaterThanOrEqualTo(260));

            Assert.That(HotReloadFileSystemPath.GetFileSystemPath(path), Is.EqualTo(@"\\?\" + path));
        }

        /// <summary>
        /// What: a long Windows UNC path is converted to the extended-length UNC form.
        /// </summary>
        [Test]
        public void GetFileSystemPath_LongWindowsUncPath_ConvertsToExtendedLengthUncPath()
        {
            if (Path.DirectorySeparatorChar != '\\')
            {
                Assert.Pass("Windows extended-length path handling applies only on Windows.");
                return;
            }

            string path = @"\\server\share\" + new string('a', 260);
            string expected = @"\\?\UNC\" + path.Substring(2);

            Assert.That(HotReloadFileSystemPath.GetFileSystemPath(path), Is.EqualTo(expected));
        }

        /// <summary>
        /// What: an already extended-length Windows path is not prefixed a second time.
        /// </summary>
        [Test]
        public void GetFileSystemPath_AlreadyExtendedWindowsPath_ReturnsUnchangedPath()
        {
            if (Path.DirectorySeparatorChar != '\\')
            {
                Assert.Pass("Windows extended-length path handling applies only on Windows.");
                return;
            }

            string path = @"\\?\C:\" + new string('a', 260);

            Assert.That(HotReloadFileSystemPath.GetFileSystemPath(path), Is.EqualTo(path));
        }

        /// <summary>
        /// What: non-Windows paths remain unchanged regardless of length.
        /// </summary>
        [Test]
        public void GetFileSystemPath_LongNonWindowsPath_ReturnsUnchangedPath()
        {
            if (Path.DirectorySeparatorChar == '\\')
            {
                Assert.Pass("Non-Windows path behavior cannot be exercised on Windows.");
                return;
            }

            string path = "/" + new string('a', 300);

            Assert.That(HotReloadFileSystemPath.GetFileSystemPath(path), Is.EqualTo(path));
        }
    }
}
