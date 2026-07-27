#if ULOOP_HAS_INPUT_SYSTEM
#nullable enable
using System.Collections.Generic;
using UnityEngine.InputSystem;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Outcome of parsing a comma-separated key filter: the keys to record, plus every entry that
    /// named no key. Why carry the rejected entries instead of dropping them: a filter that lost
    /// entries records something other than what was asked for, and the caller cannot tell that
    /// from a filter that was never given.
    /// </summary>
    internal sealed class KeyFilterParseResult
    {
        public KeyFilterParseResult(HashSet<Key>? filter, IReadOnlyList<string> invalidKeyNames)
        {
            Filter = filter;
            InvalidKeyNames = invalidKeyNames;
        }

        public HashSet<Key>? Filter { get; }

        public IReadOnlyList<string> InvalidKeyNames { get; }
    }
}
#endif
