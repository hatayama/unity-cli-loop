using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Reads the current Play Mode view into a reusable Texture2D.
    /// </summary>
    internal interface IGameViewFrameSource
    {
        bool TryReadFrame(Texture2D destination);

        bool TryGetSize(out int width, out int height);
    }
}
