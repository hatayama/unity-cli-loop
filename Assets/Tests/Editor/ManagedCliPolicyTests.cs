using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies managed CLI policy resolution and guidance visibility.
    /// </summary>
    public class ManagedCliPolicyTests
    {
        /// <summary>
        /// Verifies that executable paths resolve to Homebrew, winget, or no package manager.
        /// </summary>
        [TestCase("/opt/homebrew/Cellar/uloop/3.1.0/bin/uloop", ManagedCliKind.Homebrew)]
        [TestCase(@"C:\Program Files\WinGet\Links\uloop.exe", ManagedCliKind.Winget)]
        [TestCase(@"C:\Users\<USER_NAME>\AppData\Local\Programs\uloop\bin\uloop.exe", ManagedCliKind.None)]
        [TestCase("/opt/homebrew/Cellar/uloop/3.1.0/WinGet/Links/uloop", ManagedCliKind.Homebrew)]
        public void Resolve_ReturnsExpectedKind(string executablePath, ManagedCliKind expectedKind)
        {
            ManagedCliKind result = ManagedCliPolicy.Resolve(executablePath, directoryPath => false);

            Assert.That(result, Is.EqualTo(expectedKind));
        }

        /// <summary>
        /// Verifies that guidance is shown only for an unusable package-manager-owned CLI.
        /// </summary>
        [TestCase(ManagedCliKind.Homebrew, false, true)]
        [TestCase(ManagedCliKind.Winget, false, true)]
        [TestCase(ManagedCliKind.Homebrew, true, false)]
        [TestCase(ManagedCliKind.None, false, false)]
        public void ShouldShowUpgradeGuidance_ReturnsExpectedValue(
            ManagedCliKind kind,
            bool isCliUsable,
            bool expected)
        {
            bool result = ManagedCliPolicy.ShouldShowUpgradeGuidance(kind, isCliUsable);

            Assert.That(result, Is.EqualTo(expected));
        }
    }
}
