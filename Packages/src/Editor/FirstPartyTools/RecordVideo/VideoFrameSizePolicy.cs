namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Rounds Game View dimensions down so H.264 can encode them.
    /// </summary>
    internal static class VideoFrameSizePolicy
    {
        internal static int RoundDownToEven(int size)
        {
            return size & ~1;
        }
    }
}
