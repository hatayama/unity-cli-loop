using System;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Presentation;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies CLI install progress text formatting.
    /// </summary>
    public class CliInstallProgressFormattingTests
    {
        [Test]
        public void FormatStatusLine_WhenAnimationStepZero_UsesOneVisibleDotWithTransparentPadding()
        {
            // Verifies one visible dot keeps three-dot width via transparent rich-text padding.
            string result = CliInstallProgressFormatting.FormatStatusLine(TimeSpan.Zero, 0);

            Assert.That(result, Is.EqualTo("Installing.<color=#00000000>..</color> (0s)"));
        }

        [Test]
        public void FormatStatusLine_WhenAnimationStepOne_UsesTwoVisibleDotsWithTransparentPadding()
        {
            // Verifies two visible dots keep three-dot width via one transparent padding dot.
            string result = CliInstallProgressFormatting.FormatStatusLine(TimeSpan.Zero, 1);

            Assert.That(result, Is.EqualTo("Installing..<color=#00000000>.</color> (0s)"));
        }

        [Test]
        public void FormatStatusLine_WhenAnimationStepTwo_UsesThreeVisibleDotsWithoutPadding()
        {
            // Verifies three visible dots need no transparent padding and match the full status form.
            string result = CliInstallProgressFormatting.FormatStatusLine(TimeSpan.Zero, 2);

            Assert.That(result, Is.EqualTo("Installing... (0s)"));
        }

        [Test]
        public void FormatStatusLine_WhenUnderOneMinute_ReturnsSecondsOnly()
        {
            // Verifies sub-minute elapsed time stays in the seconds-only format.
            string result = CliInstallProgressFormatting.FormatStatusLine(TimeSpan.FromSeconds(59), 2);

            Assert.That(result, Is.EqualTo("Installing... (59s)"));
        }

        [Test]
        public void FormatStatusLine_WhenOverOneMinute_ReturnsMinutesAndSeconds()
        {
            // Verifies minute formatting pads seconds to two digits with fixed three-dot width.
            string result = CliInstallProgressFormatting.FormatStatusLine(TimeSpan.FromSeconds(65), 2);

            Assert.That(result, Is.EqualTo("Installing... (1m 05s)"));
        }

        [Test]
        public void FormatStatusLine_WhenExactTensOfMinutes_PadsSeconds()
        {
            // Verifies exact minute boundaries still show zero-padded seconds.
            string result = CliInstallProgressFormatting.FormatStatusLine(TimeSpan.FromSeconds(600), 2);

            Assert.That(result, Is.EqualTo("Installing... (10m 00s)"));
        }

        [Test]
        public void FormatStatusLine_WhenAnimationStepThree_LoopsBackToOneVisibleDot()
        {
            // Verifies animationStep wraps every three steps so the visible-dot cycle repeats.
            string result = CliInstallProgressFormatting.FormatStatusLine(TimeSpan.Zero, 3);

            Assert.That(result, Is.EqualTo("Installing.<color=#00000000>..</color> (0s)"));
        }

        [Test]
        public void FormatDetailLine_WhenNullOrWhitespace_ReturnsEmpty()
        {
            // Verifies blank installer lines do not update the detail label.
            Assert.That(CliInstallProgressFormatting.FormatDetailLine(null), Is.EqualTo(string.Empty));
            Assert.That(CliInstallProgressFormatting.FormatDetailLine("   "), Is.EqualTo(string.Empty));
        }

        [Test]
        public void FormatDetailLine_WhenPadded_TrimsWhitespace()
        {
            // Verifies surrounding whitespace is stripped from streamed installer output.
            string result = CliInstallProgressFormatting.FormatDetailLine("  Downloading uloop  ");

            Assert.That(result, Is.EqualTo("Downloading uloop"));
        }

        [Test]
        public void InitialDetailLine_IsNonBlankAndSurvivesDetailFormatting()
        {
            // Verifies the Show() placeholder is visible immediately and would not be filtered out as a blank detail line.
            Assert.That(CliInstallProgressFormatting.INITIAL_DETAIL_LINE, Is.EqualTo("Preparing installer..."));
            Assert.That(
                CliInstallProgressFormatting.FormatDetailLine(CliInstallProgressFormatting.INITIAL_DETAIL_LINE),
                Is.EqualTo(CliInstallProgressFormatting.INITIAL_DETAIL_LINE));
        }
    }
}
