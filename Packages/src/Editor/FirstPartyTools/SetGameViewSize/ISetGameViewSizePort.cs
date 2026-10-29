using io.github.hatayama.UnityCliLoop.InternalAPIBridge;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Editor-side operations the set-game-view-size use case needs, kept behind a port so the
    /// decision logic can be exercised without a Game View.
    /// </summary>
    internal interface ISetGameViewSizePort
    {
        PlayModeViewKind GetMainViewKind();

        /// <summary>
        /// Rendering resolution of the main view. Precondition: <see cref="GetMainViewKind"/> is not
        /// None. Never opens a window.
        /// </summary>
        (uint Width, uint Height) GetRenderingResolution();

        /// <summary>
        /// Applies the owned size entry. Precondition: the kind is GameView or None; a Game View is
        /// opened when none exists. Postcondition: the main view is a Game View whose selected entry
        /// is the owned entry at width x height.
        /// </summary>
        void ApplyCustomResolution(uint width, uint height);

        /// <summary>Display text of the selected size entry. Precondition: the kind is GameView.</summary>
        string GetSelectedSizeDisplayText();

        /// <summary>
        /// Base text of the selected size entry ("uloop" for the owned entry). Precondition: the kind
        /// is GameView.
        /// </summary>
        string GetSelectedSizeBaseText();

        /// <summary>
        /// Removes every owned entry (base text "uloop", fixed resolution) from every platform group
        /// and saves the list when any was removed. Returns the count. Works with any kind.
        /// </summary>
        int RemoveOwnedSizes();

        /// <summary>
        /// Selects the entry whose display text matches and refreshes the rendering size.
        /// Precondition: the kind is GameView. Returns false, changing nothing, when no entry matches.
        /// </summary>
        bool TrySelectSize(string displayText);

        /// <summary>
        /// Selects the first entry of the current group and returns its display text.
        /// Precondition: the kind is GameView.
        /// </summary>
        string SelectFirstSize();

        /// <summary>Remembered display text for the current platform group, or null when none.</summary>
        string ReadRememberedSelection();

        void RememberSelection(string displayText);

        void ClearRememberedSelection();
    }
}
