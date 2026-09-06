using System;
using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Encodes Game View frames into a video file.
    /// </summary>
    internal interface IVideoFrameEncoder : IDisposable
    {
        int Width { get; }

        int Height { get; }

        bool AddFrame(Texture2D texture);
    }
}
