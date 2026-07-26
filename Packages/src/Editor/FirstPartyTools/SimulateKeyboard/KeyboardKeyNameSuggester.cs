using System;
using System.Collections.Generic;
#if ULOOP_HAS_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Suggests Input System Key enum names for unknown simulate-keyboard key inputs.
    /// </summary>
    internal static class KeyboardKeyNameSuggester
    {
        private const int MaxSuggestions = 6;

        public static IReadOnlyList<string> Suggest(string invalidKeyName)
        {
            if (string.IsNullOrWhiteSpace(invalidKeyName))
            {
                return Array.Empty<string>();
            }

#if !ULOOP_HAS_INPUT_SYSTEM
            return Array.Empty<string>();
#else
            string trimmed = invalidKeyName.Trim();
            List<string> suggestions = new();

            // Why not char.IsDigit: it is true for non-ASCII digits such as "３", which have no
            // Digit/Numpad enum name, so suggesting them would point at keys that do not exist.
            if (trimmed.Length == 1 && trimmed[0] >= '0' && trimmed[0] <= '9')
            {
                string digit = trimmed;
                AddUnique(suggestions, $"Digit{digit}");
                AddUnique(suggestions, $"Numpad{digit}");
            }

            string[] allNames = Enum.GetNames(typeof(Key));
            StringComparison comparison = StringComparison.OrdinalIgnoreCase;
            for (int index = 0; index < allNames.Length; index++)
            {
                string name = allNames[index];
                if (name.Contains(trimmed, comparison))
                {
                    AddUnique(suggestions, name);
                    if (suggestions.Count >= MaxSuggestions)
                    {
                        break;
                    }
                }
            }

            return suggestions;
#endif
        }

#if ULOOP_HAS_INPUT_SYSTEM
        private static void AddUnique(List<string> suggestions, string candidate)
        {
            for (int index = 0; index < suggestions.Count; index++)
            {
                if (string.Equals(suggestions[index], candidate, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            suggestions.Add(candidate);
        }
#endif
    }
}
