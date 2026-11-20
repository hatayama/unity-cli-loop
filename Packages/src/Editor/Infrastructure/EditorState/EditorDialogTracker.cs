using System.Collections.Generic;
using System.Linq;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Keeps the modal dialogs that are open in the Editor, in the order they opened, so uloop status
    /// can report them while the dialogs block the main thread.
    /// </summary>
    internal sealed class EditorDialogTracker
    {
        // Why one lock for every member: dialogs are recorded on the main thread and read by the
        // IPC thread that answers uloop status.
        private readonly object _lock = new object();
        private readonly List<(int Id, EditorDialogInfo Info)> _openDialogs = new List<(int Id, EditorDialogInfo Info)>();

        internal void RecordShown(int id, string title, string message, IReadOnlyList<string> buttons)
        {
            EditorDialogInfo info = new EditorDialogInfo(title, message, buttons);
            lock (_lock)
            {
                int index = _openDialogs.FindIndex(entry => entry.Id == id);
                if (index >= 0)
                {
                    _openDialogs[index] = (id, info);
                    return;
                }

                _openDialogs.Add((id, info));
            }
        }

        internal void RecordDismissed(int id)
        {
            lock (_lock)
            {
                _openDialogs.RemoveAll(entry => entry.Id == id);
            }
        }

        internal EditorDialogInfo[] GetOpenDialogs()
        {
            lock (_lock)
            {
                // Why sharing the infos is safe: EditorDialogInfo is immutable; only the array is new.
                return _openDialogs.Select(entry => entry.Info).ToArray();
            }
        }
    }
}
