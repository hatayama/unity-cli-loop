using System.Collections.Concurrent;
using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Holds values that dynamic-code snippets explicitly preserve for a later response.
    /// </summary>
    public static class UloopDynamicCodePartialResults
    {
        private static readonly ConcurrentDictionary<string, string> Entries = new();

        /// <summary>
        /// Records a value that remains available if the current dynamic-code execution later fails.
        /// </summary>
        public static void Set(string name, object value)
        {
            System.Diagnostics.Debug.Assert(
                !string.IsNullOrWhiteSpace(name),
                "Partial result names must not be null, empty, or whitespace.");
            Entries[name] = value?.ToString() ?? "null";
        }

        /// <summary>
        /// Copies the values captured by the current dynamic-code execution.
        /// </summary>
        internal static Dictionary<string, string> Snapshot()
        {
            return new Dictionary<string, string>(Entries);
        }

        /// <summary>
        /// Removes values captured by the preceding dynamic-code execution.
        /// </summary>
        internal static void Clear()
        {
            Entries.Clear();
        }
    }
}
