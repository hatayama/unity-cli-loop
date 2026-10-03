using System;
using UnityEngine;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.Infrastructure;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies package-manager ownership through the native CLI path resolver.
    /// </summary>
    public class NativeCliInstallPathResolverTests
    {
        /// <summary>
        /// Verifies the real resolver wiring returns winget, Homebrew, and unmanaged kinds.
        /// </summary>
        [TestCase(
            @"C:\Users\<USER_NAME>\AppData\Local\Microsoft\WinGet\Links\uloop.exe",
            ManagedCliKind.Winget)]
        [TestCase("/opt/homebrew/Cellar/uloop/3.1.0/bin/uloop", ManagedCliKind.Homebrew)]
        [TestCase(@"C:\Tools\uloop.exe", ManagedCliKind.None)]
        public void ResolveManagedCliKind_ReturnsExpectedKind(string executablePath, ManagedCliKind expectedKind)
        {
            ManagedCliKind result = NativeCliInstallPathResolver.ResolveManagedCliKind(executablePath);

            Assert.That(result, Is.EqualTo(expectedKind));
        }

        /// <summary>
        /// Verifies that a missing PATH value becomes just the install directory.
        /// </summary>
        [Test]
        public void BuildPathWithInstallDirectory_WhenCurrentPathIsNull_ReturnsInstallDirectoryOnly()
        {
            string result = NativeCliInstallPathResolver.BuildPathWithInstallDirectory(
                null,
                "/opt/uloop-test/bin",
                RuntimePlatform.OSXEditor);

            Assert.That(result, Is.EqualTo("/opt/uloop-test/bin"));
        }

        /// <summary>
        /// Verifies that removing the install directory also drops whitespace-only PATH entries.
        /// </summary>
        [Test]
        public void BuildPathWithoutInstallDirectory_WhenPathHasWhitespaceEntry_DropsItWithInstallDirectory()
        {
            string result = NativeCliInstallPathResolver.BuildPathWithoutInstallDirectory(
                "/usr/bin: :/opt/uloop-test/bin:/bin",
                "/opt/uloop-test/bin",
                RuntimePlatform.OSXEditor);

            Assert.That(result, Is.EqualTo("/usr/bin:/bin"));
        }

        /// <summary>
        /// Verifies that Windows has no default install directory when LOCALAPPDATA is unavailable.
        /// </summary>
        [Test]
        public void GetDefaultInstallDirectoryFromRoots_OnWindowsWithoutLocalAppData_ReturnsNull()
        {
            string result = NativeCliInstallPathResolver.GetDefaultInstallDirectoryFromRoots(
                RuntimePlatform.WindowsEditor,
                "/home/<USER_NAME>",
                null);

            Assert.That(result, Is.Null);
        }

        /// <summary>
        /// Verifies that POSIX has no default install directory when the home directory is blank.
        /// </summary>
        [Test]
        public void GetDefaultInstallDirectoryFromRoots_OnMacWithBlankHome_ReturnsNull()
        {
            string result = NativeCliInstallPathResolver.GetDefaultInstallDirectoryFromRoots(
                RuntimePlatform.OSXEditor,
                "   ",
                "<LOCAL_APP_DATA>");

            Assert.That(result, Is.Null);
        }

        /// <summary>
        /// Verifies that a missing executable path is never treated as the package-owned install.
        /// </summary>
        [Test]
        public void IsPackageOwnedInstallPath_WhenExecutablePathIsNull_ReturnsFalse()
        {
            bool result = NativeCliInstallPathResolver.IsPackageOwnedInstallPath(
                null,
                "/opt/uloop-test/bin",
                RuntimePlatform.OSXEditor);

            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Verifies that the PATH variable name follows each platform's spelling.
        /// </summary>
        [TestCase(RuntimePlatform.WindowsEditor, "Path")]
        [TestCase(RuntimePlatform.OSXEditor, "PATH")]
        [TestCase(RuntimePlatform.LinuxEditor, "PATH")]
        public void GetPathEnvironmentVariableName_WhenPlatformVaries_ReturnsPlatformSpelling(RuntimePlatform platform, string expectedName)
        {
            string result = NativeCliInstallPathResolver.GetPathEnvironmentVariableName(platform);

            Assert.That(result, Is.EqualTo(expectedName));
        }
    }
}
