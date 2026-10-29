using UnityEditor;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.InternalAPIBridge;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Live Editor implementation of <see cref="ISetGameViewSizePort"/> on top of the internal bridge
    /// and public Editor APIs.
    /// </summary>
    internal sealed class EditorSetGameViewSizePort : ISetGameViewSizePort
    {
        // One key per platform group, because each group keeps its own selected entry.
        private const string RememberedSelectionKeyPrefix =
            "io.github.hatayama.uloopmcp.setGameViewSize.previousSelection.";

        public PlayModeViewKind GetMainViewKind()
        {
            return PlayModeViewSwitchBridge.GetMainViewKind();
        }

        public (uint Width, uint Height) GetRenderingResolution()
        {
            Debug.Assert(
                GetMainViewKind() != PlayModeViewKind.None,
                "a Play Mode view must exist; reading would open one otherwise");
            PlayModeWindow.GetRenderingResolution(out uint width, out uint height);
            return (width, height);
        }

        public void ApplyCustomResolution(uint width, uint height)
        {
            PlayModeViewKind kind = GetMainViewKind();
            Debug.Assert(
                kind == PlayModeViewKind.GameView || kind == PlayModeViewKind.None,
                "custom resolution applies only to a Game View");
            GameViewSizeListBridge.ApplyCustomSize(width, height, SetGameViewSizeUseCase.OwnedSizeBaseName);
        }

        public string GetSelectedSizeDisplayText()
        {
            return GameViewSizeListBridge.GetSelectedSizeDisplayText();
        }

        public string GetSelectedSizeBaseText()
        {
            return GameViewSizeListBridge.GetSelectedSizeBaseText();
        }

        public int RemoveOwnedSizes()
        {
            return GameViewSizeListBridge.RemoveCustomSizes(SetGameViewSizeUseCase.OwnedSizeBaseName);
        }

        public bool TrySelectSize(string displayText)
        {
            return GameViewSizeListBridge.TrySelectSize(displayText);
        }

        public string SelectFirstSize()
        {
            return GameViewSizeListBridge.SelectFirstSize();
        }

        public string ReadRememberedSelection()
        {
            string remembered = SessionState.GetString(RememberedSelectionKey(), string.Empty);
            return string.IsNullOrEmpty(remembered) ? null : remembered;
        }

        public void RememberSelection(string displayText)
        {
            SessionState.SetString(RememberedSelectionKey(), displayText);
        }

        public void ClearRememberedSelection()
        {
            SessionState.SetString(RememberedSelectionKey(), string.Empty);
        }

        private static string RememberedSelectionKey()
        {
            return RememberedSelectionKeyPrefix + GameViewSizeListBridge.GetCurrentGroupName();
        }
    }
}
