#if UNITY_EDITOR
using System;
using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.Runtime
{
    /// <summary>
    /// Reports the number of pause point entries cleared by a bulk clear request.
    /// </summary>
    internal sealed class UloopPausePointClearAllResult
    {
        public UloopPausePointClearAllResult(
            int clearedCount,
            DateTime clearedAtUtc,
            UloopPausePointEditorStateSnapshot editorState,
            string[] clearedIds,
            bool resumedFromPause)
        {
            Debug.Assert(editorState != null, "editorState must not be null");
            Debug.Assert(clearedIds != null, "clearedIds must not be null");

            ClearedCount = clearedCount;
            ClearedAtUtc = clearedAtUtc;
            EditorState = editorState;
            ClearedIds = clearedIds;
            ResumedFromPause = resumedFromPause;
        }

        public int ClearedCount { get; }
        public DateTime ClearedAtUtc { get; }
        public UloopPausePointEditorStateSnapshot EditorState { get; }
        public string[] ClearedIds { get; }

        // True when the bulk clear actually resumed a pause-point-owned Editor pause, so callers
        // can warn that Play Mode was resumed as a side effect. False when no pause window was
        // open (e.g. nothing was paused, or only a manual pause the clear intentionally preserved).
        public bool ResumedFromPause { get; }
    }
}
#endif
