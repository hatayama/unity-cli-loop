using UnityEditor;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Answers quit-editor; its Success is the only payload.
    /// </summary>
    public class QuitEditorResponse : UnityCliLoopToolResponse
    {
    }

    /// <summary>
    /// Runs the quit-editor command behind uloop launch --restart and --quit: closes the Editor so that it
    /// saves its window layout, which a force-kill loses.
    /// </summary>
    internal static class QuitEditorBridgeCommand
    {
        // Long enough for this answer to be written before the process ends.
        private const double ExitDelaySeconds = 0.5;

        public static QuitEditorResponse Execute()
        {
            // Why Exit rather than the quit the Editor menu runs: that quit asks whether to save modified
            // scenes and waits for an answer nobody gives, while Exit discards them without asking and still
            // saves the window layout.
            DelayedEditorExit delayedExit = new(
                EditorApplication.timeSinceStartup + ExitDelaySeconds,
                EditorApplication.Exit);
            EditorApplication.CallbackFunction exitWhenDue = null;
            exitWhenDue = () =>
            {
                if (!delayedExit.ExitIfDue(EditorApplication.timeSinceStartup))
                {
                    return;
                }

                EditorApplication.update -= exitWhenDue;
            };
            // Why update rather than delayCall: an Editor in the background that is not repainting does not
            // run delayCall, so the exit would never come.
            EditorApplication.update += exitWhenDue;
            return new QuitEditorResponse();
        }
    }
}
