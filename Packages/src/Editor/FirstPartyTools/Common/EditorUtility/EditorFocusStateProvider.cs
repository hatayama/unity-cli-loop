using UnityEditor;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Reads whether the Unity Editor is the active application.
    /// </summary>
    public interface IEditorFocusStateProvider
    {
        bool IsFocused { get; }
    }

    /// <summary>
    /// Reads the current focus state from the Unity Editor API.
    /// </summary>
    public sealed class EditorFocusStateProvider : IEditorFocusStateProvider
    {
        public bool IsFocused => EditorApplication.isFocused;
    }
}
