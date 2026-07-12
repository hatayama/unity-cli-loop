using System;
using UnityEditor;
using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Reads the Unity Editor state used by the watch step monitor.
    /// </summary>
    public sealed class UnityWatchEditorStateProvider : IWatchEditorStateProvider
    {
        public bool IsPlaying => EditorApplication.isPlaying;
        public bool IsPaused => EditorApplication.isPaused;
        public int FrameCount => Time.frameCount;
        public DateTime UtcNow => DateTime.UtcNow;
    }
}
