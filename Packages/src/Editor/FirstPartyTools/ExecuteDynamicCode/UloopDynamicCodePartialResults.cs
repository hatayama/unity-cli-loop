using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Holds values that dynamic-code snippets explicitly preserve for a later response.
    /// </summary>
    public static class UloopDynamicCodePartialResults
    {
        private static readonly ConcurrentDictionary<string, string> Entries = new();
        private static readonly AsyncLocal<int> ExecutionGeneration = new();
        private static int _currentGeneration;

        /// <summary>
        /// Records a value that remains available if the current dynamic-code execution later fails.
        /// </summary>
        public static void Set(string name, object value)
        {
            System.Diagnostics.Debug.Assert(
                !string.IsNullOrWhiteSpace(name),
                "Partial result names must not be null, empty, or whitespace.");
            int currentGeneration = Volatile.Read(ref _currentGeneration);
            if (currentGeneration == 0 || ExecutionGeneration.Value != currentGeneration)
            {
                return;
            }

            Entries[name] = value?.ToString() ?? "null";
        }

        /// <summary>
        /// Starts an isolated holder lifetime for one dynamic-code execution.
        /// </summary>
        internal static void OpenExecutionScope()
        {
            int nextGeneration = Interlocked.Increment(ref _currentGeneration);
            ExecutionGeneration.Value = nextGeneration;
            Entries.Clear();
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
