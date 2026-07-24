using System;
using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.Presentation
{
    /// <summary>
    /// Formats progress status text shown while the global CLI installer is running.
    /// </summary>
    internal static class CliInstallProgressFormatting
    {
        // Why a non-empty placeholder: the installer bootstrap stays silent for seconds
        // (process start, script download) before its first stdout line arrives, and an
        // empty detail label during that window reads as a hang.
        internal const string INITIAL_DETAIL_LINE = "Preparing installer...";

        // Why transparent rich-text dots: keep "Installing... (Ns)" word order while
        // reserving three-dot width so the elapsed-time suffix does not shift left/right.
        private const string HIDDEN_DOT_OPEN = "<color=#00000000>";
        private const string HIDDEN_DOT_CLOSE = "</color>";

        internal static string FormatStatusLine(TimeSpan elapsed, int animationStep)
        {
            Debug.Assert(elapsed >= TimeSpan.Zero, "elapsed must not be negative");
            Debug.Assert(animationStep >= 0, "animationStep must not be negative");

            int visibleDotCount = (animationStep % 3) + 1;
            int hiddenDotCount = 3 - visibleDotCount;
            string dots = BuildPaddedDots(visibleDotCount, hiddenDotCount);

            int totalSeconds = (int)elapsed.TotalSeconds;
            if (totalSeconds < 60)
            {
                return $"Installing{dots} ({totalSeconds}s)";
            }

            int minutes = totalSeconds / 60;
            int seconds = totalSeconds % 60;
            return $"Installing{dots} ({minutes}m {seconds:00}s)";
        }

        internal static string FormatDetailLine(string rawLine)
        {
            if (string.IsNullOrWhiteSpace(rawLine))
            {
                return string.Empty;
            }

            return rawLine.Trim();
        }

        private static string BuildPaddedDots(int visibleDotCount, int hiddenDotCount)
        {
            Debug.Assert(visibleDotCount >= 1 && visibleDotCount <= 3, "visibleDotCount must be 1..3");
            Debug.Assert(hiddenDotCount >= 0 && hiddenDotCount <= 2, "hiddenDotCount must be 0..2");
            Debug.Assert(visibleDotCount + hiddenDotCount == 3, "visible and hidden dots must total 3");

            string visibleDots = new string('.', visibleDotCount);
            if (hiddenDotCount == 0)
            {
                return visibleDots;
            }

            string hiddenDots = new string('.', hiddenDotCount);
            return visibleDots + HIDDEN_DOT_OPEN + hiddenDots + HIDDEN_DOT_CLOSE;
        }
    }
}
