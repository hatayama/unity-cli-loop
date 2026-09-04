using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Classifies Unity Console entry mode flags without depending on Editor state.
    /// </summary>
    internal sealed class ConsoleLogModeClassifier
    {
        private const int AssertModeFlag = 1 << 1;
        private const int ScriptingAssertionModeFlag = 1 << 21;

        /// <summary>
        /// Converts Unity's internal log mode to a LogType enum.
        /// </summary>
        internal LogType Classify(int mode)
        {
            // Assertion modes omit the scripting severity bits inspected below, so classify
            // their dedicated flags first instead of allowing them to fall back to Log.
            if ((mode & (AssertModeFlag | ScriptingAssertionModeFlag)) != 0)
            {
                return LogType.Assert;
            }

            // Analyze the observed patterns:
            // 0x804400 = Log
            // 0x804200 = Warning
            // 0x804100 = Error

            // Extract bits 8-11 (shift right 8, mask 4 bits)
            int logType = (mode >> 8) & 0xF;

            LogType result = logType switch
            {
                0x01 => LogType.Error, // 0x804100 -> (0x4100 >> 8) & 0xF = 0x41 & 0xF = 0x1
                0x02 => LogType.Warning, // 0x804200 -> (0x4200 >> 8) & 0xF = 0x42 & 0xF = 0x2
                0x04 => LogType.Log, // 0x804400 -> (0x4400 >> 8) & 0xF = 0x44 & 0xF = 0x4
                _ => DetermineLogTypeFromModeAnalysis(mode)
            };

            return result;
        }

        /// <summary>
        /// Analyzes mode values to determine log type when standard mapping fails.
        /// </summary>
        private LogType DetermineLogTypeFromModeAnalysis(int mode)
        {
            // Analyze the mode values we've seen:
            // 8406016 (0x802000) - appears to be Log type
            // 8405504 (0x801E00) - appears to be Warning type
            // 8405248 (0x801D00) - appears to be Error type

            // Let's try different bit extraction strategies
            // int lowerBits = mode & 0x7; // Lower 3 bits
            // int midBits = (mode >> 8) & 0x7; // Bits 8-10
            // int higherBits = (mode >> 16) & 0x7; // Bits 16-18

            // Based on observed patterns, try to determine type
            if (mode == 8406016) return LogType.Log; // This is a test log message
            if (mode == 8405504) return LogType.Warning; // This is a test warning message
            if (mode == 8405248) return LogType.Error; // This is a test error message

            return LogType.Log; // Default to Log for unknown types
        }
    }
}
