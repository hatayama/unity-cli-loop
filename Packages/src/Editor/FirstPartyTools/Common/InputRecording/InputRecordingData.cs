#nullable enable
using System;
using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Holds serialized data for Input Recording behavior.
    /// </summary>
    [Serializable]
    internal class InputRecordingData
    {
        public InputRecordingMetadata Metadata { get; set; } = new();
        public List<InputFrameEvents> Frames { get; set; } = new();

        public int GetTotalEventCount()
        {
            int count = 0;
            for (int i = 0; i < Frames.Count; i++)
            {
                count += Frames[i].Events.Count;
            }
            return count;
        }
    }

    /// <summary>
    /// Provides Input Recording Metadata behavior for Unity CLI Loop.
    /// </summary>
    [Serializable]
    internal class InputRecordingMetadata
    {
        public string RecordedAt { get; set; } = "";
        public int TotalFrames { get; set; }
        public float DurationSeconds { get; set; }
    }

    /// <summary>
    /// Provides Input Frame Events behavior for Unity CLI Loop.
    /// </summary>
    [Serializable]
    internal class InputFrameEvents
    {
        public int Frame { get; set; }
        public List<RecordedInputEvent> Events { get; set; } = new();
    }

    /// <summary>
    /// Provides Recorded Input Event behavior for Unity CLI Loop.
    /// </summary>
    [Serializable]
    internal class RecordedInputEvent
    {
        public string Type { get; set; } = "";
        public string Data { get; set; } = "";
    }
}
