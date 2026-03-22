#nullable enable
using System;
using System.Collections.Generic;

namespace io.github.hatayama.uLoopMCP
{
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

    [Serializable]
    internal class InputRecordingMetadata
    {
        public string RecordedAt { get; set; } = "";
        public int TotalFrames { get; set; }
        public float DurationSeconds { get; set; }
    }

    [Serializable]
    internal class InputFrameEvents
    {
        public int Frame { get; set; }
        public List<RecordedInputEvent> Events { get; set; } = new();
    }

    [Serializable]
    internal class RecordedInputEvent
    {
        public string Type { get; set; } = "";
        public string Data { get; set; } = "";
    }
}
