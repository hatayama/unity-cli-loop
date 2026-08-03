using System.Diagnostics;
using System.Text;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Regression coverage for <see cref="HotReloadProcessRunner"/> stream decoding: worker
    /// output must be decoded as UTF-8 so localized compiler diagnostics survive on systems
    /// whose default code page is not UTF-8 (e.g. Japanese Windows).
    /// </summary>
    public class HotReloadProcessRunnerTests
    {
        /// <summary>
        /// What: CreateStartInfo redirects both worker streams and decodes them as UTF-8,
        /// matching the bundled .NET host's redirected console output encoding.
        /// </summary>
        [Test]
        public void CreateStartInfo_DecodesRedirectedStreamsAsUtf8()
        {
            ProcessStartInfo startInfo = HotReloadProcessRunner.CreateStartInfo(
                "worker-host", "worker-arguments", ".");

            Assert.That(startInfo.StandardOutputEncoding, Is.EqualTo(Encoding.UTF8));
            Assert.That(startInfo.StandardErrorEncoding, Is.EqualTo(Encoding.UTF8));
            Assert.That(startInfo.RedirectStandardOutput, Is.True);
            Assert.That(startInfo.RedirectStandardError, Is.True);
            Assert.That(startInfo.UseShellExecute, Is.False);
        }
    }
}
