#if ULOOP_HAS_INPUT_SYSTEM
#nullable enable

using System.Collections.Generic;
using UnityEngine.InputSystem;

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
            string keyStateReadUpdateType,
            IReadOnlyList<Key> releasedInputKeys)
        {
            ReleasedKeys = releasedKeys;
            ReleasedKeyStates = releasedKeyStates;
            KeyStateReadUpdateType = keyStateReadUpdateType;
            ReleasedInputKeys = releasedInputKeys;
        }

        internal IReadOnlyList<string> ReleasedKeys { get; }
        internal IReadOnlyList<ReleasedKeyState> ReleasedKeyStates { get; }
        internal string KeyStateReadUpdateType { get; }
        internal IReadOnlyList<Key> ReleasedInputKeys { get; }
    }
}
#endif
