using System.Diagnostics;
using System.Runtime.InteropServices;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies Process Start Helper behavior.
    /// </summary>
    [TestFixture]
    public class ProcessStartHelperTests
    {
        [Test]
        public void TryStart_NonExistentExecutable_ReturnsNull()
        {
            ProcessStartInfo startInfo = new()            {
                FileName = "/nonexistent/path/to/executable",
                UseShellExecute = false,
                CreateNoWindow = true
            };

            Process result = ProcessStartHelper.TryStart(startInfo);

            Assert.IsNull(result);
        }

        [Test]
        public void TryStart_ValidExecutable_ReturnsNonNullProcess()
        {
            bool isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
            ProcessStartInfo startInfo = new()            {
                FileName = isWindows ? "cmd.exe" : "/bin/echo",
                Arguments = isWindows ? "/c echo test" : "test",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };

            Process result = ProcessStartHelper.TryStart(startInfo);

            Assert.IsNotNull(result);
            result.WaitForExit();
            result.Dispose();
        }
    }
}
