#nullable enable

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Device readback for one key after a ReleaseAll injection.
    /// </summary>
    public sealed class ReleasedKeyState
    {
        public string Key { get; set; } = "";
        public bool DeviceIsPressedAfterRelease { get; set; }
    }
}
