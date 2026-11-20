using System;
#if UNITY_6000_7_OR_NEWER
using UnityEditor;
#endif

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Feeds <see cref="EditorDialogTracker"/> from UnityEditor.EditorDialogEvents, which Unity 6000.7
    /// and later raise around every message box and modal Editor window. Older Editors have no such
    /// events, so the tracker stays empty there.
    /// </summary>
    internal sealed class EditorDialogEventsSubscriber
    {
        private readonly EditorDialogTracker _tracker;

        internal EditorDialogEventsSubscriber(EditorDialogTracker tracker)
        {
            _tracker = tracker ?? throw new ArgumentNullException(nameof(tracker));
        }

        internal void Subscribe()
        {
#if UNITY_6000_7_OR_NEWER
            // Why no unsubscribe: Unity clears these events on every code reload
            // ([AutoStaticsCleanupOnCodeReload]), and the composition root subscribes once per domain.
            EditorDialogEvents.dialogWillShow += OnDialogWillShow;
            EditorDialogEvents.dialogDismissed += OnDialogDismissed;
#endif
        }

#if UNITY_6000_7_OR_NEWER
        private void OnDialogWillShow(DialogEventInfo info)
        {
            _tracker.RecordShown(info.Id, info.Title, info.Message, info.ButtonLabels);
        }

        private void OnDialogDismissed(DialogEventInfo info)
        {
            _tracker.RecordDismissed(info.Id);
        }
#endif
    }
}
