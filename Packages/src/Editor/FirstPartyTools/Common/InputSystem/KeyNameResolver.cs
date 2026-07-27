#if ULOOP_HAS_INPUT_SYSTEM
using System;
using System.Collections.Generic;
using UnityEngine.InputSystem;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Resolves Input System Key values from the key names tools accept, so every tool applies the
    /// same rule for what counts as a key name.
    /// </summary>
    internal static class KeyNameResolver
    {
        // Immutable name-to-value map of the Input System Key enum, so key resolution never falls
        // back to Enum.TryParse's ordinal and flag-combination behavior.
        private static readonly IReadOnlyDictionary<string, Key> DefinedKeysByName = BuildDefinedKeysByName();

        /// <summary>
        /// Resolves a raw key name to its Key value, reporting whether it named a key at all.
        /// </summary>
        public static (bool resolved, Key key) Resolve(string keyName)
        {
            // Why not Enum.TryParse: it also accepts ordinals ("3"), signed ordinals ("+3"),
            // whitespace-padded input, comma-separated names OR-ed together ("Space,Enter"), and
            // undefined ordinals ("300") that later throw from the keyboard indexer. Only a name
            // that is defined on the Key enum may resolve to a key.
            string normalizedKey = NormalizeKeyName(keyName);
            if (!DefinedKeysByName.TryGetValue(normalizedKey, out Key key) || key == Key.None)
            {
                return (false, Key.None);
            }

            return (true, key);
        }

        /// <summary>
        /// Trims the raw name and applies the Return alias, giving callers the form the whitelist
        /// is keyed by so their diagnostics can describe the same value that was looked up.
        /// </summary>
        public static string NormalizeKeyName(string keyName)
        {
            // Why trim here rather than at the whitelist comparison: Enum.TryParse used to accept
            // whitespace-padded names, so padded correct input already worked. Blocking ambiguous
            // input must not narrow correct input, and the alias has to see the padded form too.
            string trimmed = keyName.Trim();
            if (string.Equals(trimmed, "Return", StringComparison.OrdinalIgnoreCase))
            {
                return Key.Enter.ToString();
            }

            return trimmed;
        }

        private static IReadOnlyDictionary<string, Key> BuildDefinedKeysByName()
        {
            string[] names = Enum.GetNames(typeof(Key));
            Array values = Enum.GetValues(typeof(Key));
            Dictionary<string, Key> keysByName = new(names.Length, StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < names.Length; index++)
            {
                keysByName[names[index]] = (Key)values.GetValue(index);
            }

            return keysByName;
        }
    }
}
#endif
