#nullable enable

using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Result of ReleaseAllKeysImmediately, including device readback after injection.
    /// </summary>
    internal sealed class ReleaseAllKeysImmediateResult
    {
        internal ReleaseAllKeysImmediateResult(
            IReadOnlyList<string> releasedKeys,
            IReadOnlyList<ReleasedKeyState> releasedKeyStates,
            string keyStateReadUpdateType)
        {
            ReleasedKeys = releasedKeys;
            ReleasedKeyStates = releasedKeyStates;
            KeyStateReadUpdateType = keyStateReadUpdateType;
        }

        internal IReadOnlyList<string> ReleasedKeys { get; }
        internal IReadOnlyList<ReleasedKeyState> ReleasedKeyStates { get; }
        internal string KeyStateReadUpdateType { get; }
    }
}
