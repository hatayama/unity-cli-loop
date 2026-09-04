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
    }
}
