using System;
using System.Collections.Generic;
using System.Linq;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// One modal dialog that is open in the Unity Editor, as reported by uloop status.
    /// </summary>
    public sealed class EditorDialogInfo
    {
        public EditorDialogInfo(string title, string message, IReadOnlyList<string> buttons)
        {
            // Why normalize instead of reject: Unity itself reports a modal Editor window without
            // a message or buttons, so null here is a reading, not a caller mistake.
            Title = title ?? string.Empty;
            Message = message ?? string.Empty;
            // Why copy: the status thread reads this after the main thread recorded it, so a later
            // change to the caller's list must not reach the report.
            Buttons = buttons == null ? Array.Empty<string>() : buttons.ToArray();
        }

        public string Title { get; }

        public string Message { get; }

        public IReadOnlyList<string> Buttons { get; }
    }
}
