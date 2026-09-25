using System;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies the enable-pause-point warning and resolve-failure text builders.
    /// </summary>
    [TestFixture]
    public sealed class PausePointEnableWarningsTests
    {
        /// <summary>
        /// What: retarget warning interpolates resolved method, requested line, and edited span.
        /// </summary>
        [Test]
        public void BuildRetargetedToHotReloadPatchWarningOrEmpty_WhenRetargeted_ReturnsFormattedWarning()
        {
            string warning = PausePointEnableWarnings.BuildRetargetedToHotReloadPatchWarningOrEmpty(
                true,
                "Example.Run",
                42,
                10,
                20);

            Assert.That(
                warning,
                Is.EqualTo(
                    string.Format(
                        SourcePausePointConstants.HotReloadRetargetedToEditedFileWarningFormat,
                        "Example.Run",
                        42,
                        10,
                        20)));
            Assert.That(warning, Does.Not.Contain("last compiled source"));
        }

        /// <summary>
        /// What: the retarget helper stays silent when the marker did not retarget.
        /// </summary>
        [Test]
        public void BuildRetargetedToHotReloadPatchWarningOrEmpty_WhenNotRetargeted_ReturnsEmpty()
        {
            string warning = PausePointEnableWarnings.BuildRetargetedToHotReloadPatchWarningOrEmpty(
                false,
                "Example.Run",
                42,
                10,
                20);

            Assert.That(warning, Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// What: nearby compiled spans are formatted as a suffix on a resolve-failure message.
        /// </summary>
        [Test]
        public void AppendNearbyCompiledMethodsSuffix_WhenNearbyMethodsExist_AppendsFormattedSpans()
        {
            string errorMessage = "No sequence point found on or after line 9999 in 'file'.";
            SourcePausePointNearbyCompiledMethod[] nearby =
            {
                new SourcePausePointNearbyCompiledMethod("CompiledMethodSpanFixture.Target", 8, 11),
                new SourcePausePointNearbyCompiledMethod("CompiledMethodSpanFixture.OtherMethod", 15, 18)
            };

            string message = PausePointEnableWarnings.AppendNearbyCompiledMethodsSuffix(errorMessage, nearby);

            Assert.That(
                message,
                Is.EqualTo(
                    errorMessage
                    + SourcePausePointConstants.NearbyCompiledMethodsPrefix
                    + "'CompiledMethodSpanFixture.Target' spans lines 8-11"
                    + "; "
                    + "'CompiledMethodSpanFixture.OtherMethod' spans lines 15-18"
                    + "."));
        }

        /// <summary>
        /// What: an empty nearby list leaves the resolve-failure message unchanged.
        /// </summary>
        [Test]
        public void AppendNearbyCompiledMethodsSuffix_WhenNearbyListIsEmpty_LeavesMessageUnchanged()
        {
            string errorMessage = "No sequence point found on or after line 9999 in 'file'.";

            string message = PausePointEnableWarnings.AppendNearbyCompiledMethodsSuffix(
                errorMessage,
                Array.Empty<SourcePausePointNearbyCompiledMethod>());

            Assert.That(message, Is.EqualTo(errorMessage));
        }
    }
}
