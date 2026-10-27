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
        public static QuitEditorResponse Execute()
        {
            // Why Exit rather than the quit the Editor menu runs: that quit asks whether to save modified
            // scenes and waits for an answer nobody gives, while Exit discards them without asking and
            // still saves the window layout.
            // Why on the next update: exiting now would end the process before this answer is written.
            EditorApplication.delayCall += () => EditorApplication.Exit(0);
            return new QuitEditorResponse();
        }
    }
}
