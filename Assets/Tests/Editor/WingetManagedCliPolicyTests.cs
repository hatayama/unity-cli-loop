using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies winget-managed CLI path classification.
    /// </summary>
    public class WingetManagedCliPolicyTests
    {
        /// <summary>
        /// Verifies that WinGet Packages and Links paths are recognized across roots, separators, and casing.
        /// </summary>
        [TestCase(@"C:\Users\<USER_NAME>\AppData\Local\Microsoft\WinGet\Packages\hatayama.uloop_Microsoft.Winget.Source_8wekyb3d8bbwe\uloop.exe")]
        [TestCase(@"C:\Users\<USER_NAME>\AppData\Local\Microsoft\WinGet\Links\uloop.exe")]
        [TestCase(@"C:\Program Files\WinGet\Packages\hatayama.uloop_Microsoft.Winget.Source_8wekyb3d8bbwe\uloop.exe")]
        [TestCase(@"C:\Program Files\WinGet\Links\uloop.exe")]
        [TestCase("C:/Users/<USER_NAME>/AppData/Local/Microsoft/WinGet/Packages/hatayama.uloop_Microsoft.Winget.Source_8wekyb3d8bbwe/uloop.exe")]
        [TestCase(@"C:\Users\<USER_NAME>\AppData\Local\Microsoft\winget\packages\hatayama.uloop\uloop.exe")]
        public void IsWingetManagedPath_WithManagedDirectory_ReturnsTrue(string executablePath)
        {
            bool result = WingetManagedCliPolicy.IsWingetManagedPath(executablePath);

            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Verifies that unrelated paths are not classified as winget-managed.
        /// </summary>
        [TestCase(@"C:\Users\<USER_NAME>\AppData\Local\Programs\uloop\bin\uloop.exe")]
        [TestCase("/opt/homebrew/Cellar/uloop/3.1.0/bin/uloop")]
        [TestCase(@"C:\Tools\WinGet\uloop.exe")]
        [TestCase(@"C:\Tools\WinGet\Other\Packages\uloop.exe")]
        [TestCase(@"D:\Packages\WinGet\uloop.exe")]
        public void IsWingetManagedPath_WithoutAdjacentManagedDirectory_ReturnsFalse(string executablePath)
        {
            bool result = WingetManagedCliPolicy.IsWingetManagedPath(executablePath);

            Assert.That(result, Is.False);
        }
    }
}
