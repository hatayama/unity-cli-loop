#nullable enable

using System.Collections.Generic;
using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Builds the ReleaseAll message suffix that reports keys still pressed after readback.
    /// Why a pure formatter: the still-pressed device view cannot always be reproduced in
    /// PlayMode, so tests pin the exact wire sentence independently of Input System state.
    /// </summary>
    internal static class SimulateKeyboardReleaseMessageFormatter
    {
        internal const string StillPressedInViewNoteFormat =
            "{0} key(s) still report pressed in the {1} view; the release may not yet be visible to gameplay polling.";

        internal const string DeferredLatchSyncScheduledNote =
            "A deferred latch sync will run on the next player input update.";

        internal static string FormatStillPressedNote(int stillPressedCount, string updateType)
        {
            Debug.Assert(stillPressedCount > 0, "still-pressed note is only for remaining pressed keys");
            Debug.Assert(!string.IsNullOrEmpty(updateType), "readback update type is required in the still-pressed note");
            return string.Format(StillPressedInViewNoteFormat, stillPressedCount, updateType);
        }

        internal static string AppendStillPressedNote(
            string message,
            IReadOnlyList<ReleasedKeyState> releasedKeyStates,
            string keyStateReadUpdateType)
        {
            if (message == null || releasedKeyStates == null)
            {
                Debug.Assert(false, "release message and released key states must exist for ReleaseAll");
                return message ?? string.Empty;
            }

            int stillPressedCount = CountStillPressed(releasedKeyStates);
            if (stillPressedCount == 0)
            {
                return message;
            }

            Debug.Assert(
                !string.IsNullOrEmpty(keyStateReadUpdateType),
                "readback update type is required when a still-pressed note is appended");
            return message + " " + FormatStillPressedNote(stillPressedCount, keyStateReadUpdateType);
        }

        private static int CountStillPressed(IReadOnlyList<ReleasedKeyState> releasedKeyStates)
        {
            int stillPressedCount = 0;
            for (int index = 0; index < releasedKeyStates.Count; index++)
            {
                if (releasedKeyStates[index].DeviceIsPressedAfterRelease)
                {
                    stillPressedCount++;
                }
            }

            return stillPressedCount;
        }

        internal static string AppendDeferredLatchSyncNote(string message, bool scheduled)
        {
            if (!scheduled)
            {
                return message ?? string.Empty;
            }

            if (message == null)
            {
                Debug.Assert(false, "release message must exist when appending the deferred latch-sync note");
                return DeferredLatchSyncScheduledNote;
            }

            return message + " " + DeferredLatchSyncScheduledNote;
        }
    }
}
