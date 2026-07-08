using System;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Compares external asset fingerprints using the shared file-state identity contract.
    /// </summary>
    internal static class ExternalAssetFileStateComparer
    {
        internal static bool HasSameFileState(
            (bool Exists, DateTime LastWriteTimeUtc, long Length) previousFingerprint,
            (bool Exists, DateTime LastWriteTimeUtc, long Length) currentFingerprint)
        {
            return previousFingerprint.Exists == currentFingerprint.Exists &&
                   previousFingerprint.LastWriteTimeUtc == currentFingerprint.LastWriteTimeUtc &&
                   previousFingerprint.Length == currentFingerprint.Length;
        }
    }
}
