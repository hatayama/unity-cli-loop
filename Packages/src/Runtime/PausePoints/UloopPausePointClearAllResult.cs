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
            UloopPausePointEditorStateSnapshot editorState)
        {
            Debug.Assert(editorState != null, "editorState must not be null");

            ClearedCount = clearedCount;
            ClearedAtUtc = clearedAtUtc;
            EditorState = editorState;
        }

        public int ClearedCount { get; }
        public DateTime ClearedAtUtc { get; }
        public UloopPausePointEditorStateSnapshot EditorState { get; }
    }
}
#endif
