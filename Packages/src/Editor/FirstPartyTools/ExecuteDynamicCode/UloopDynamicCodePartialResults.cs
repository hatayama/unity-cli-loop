using System;
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
        private static readonly ConcurrentDictionary<string, PartialResultEntry> Entries = new();
        private static readonly AsyncLocal<int> ExecutionGeneration = new();
        private static int _currentGeneration;

        /// <summary>
        /// Invoked after Set validates its generation and before it records the entry.
        /// Why: tests need to deterministically reproduce a stale execution resuming after a successor scope opens.
        /// </summary>
        internal static Action AfterGenerationValidatedForTesting { get; set; }

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

            AfterGenerationValidatedForTesting?.Invoke();
            PartialResultEntry entry = new(currentGeneration, value?.ToString() ?? "null");
            // ConcurrentDictionary reruns this factory after contention, so the comparison always sees
            // the latest entry. A get-then-set would let a stale execution replace a newer result.
            Entries.AddOrUpdate(
                name,
                entry,
                (_, existingEntry) => existingEntry.Generation > currentGeneration
                    ? existingEntry
                    : entry);
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
            int currentGeneration = Volatile.Read(ref _currentGeneration);
            Dictionary<string, string> snapshot = new();
            foreach (KeyValuePair<string, PartialResultEntry> entry in Entries)
            {
                if (entry.Value.Generation != currentGeneration)
                {
                    continue;
                }

                snapshot[entry.Key] = entry.Value.Value;
            }

            // A cancelled execution can write after a successor scope opens, so this projection,
            // not the Set-side check, defines which entries belong to the returned response.
            return snapshot;
        }

        /// <summary>
        /// Removes values captured by the preceding dynamic-code execution.
        /// </summary>
        internal static void Clear()
        {
            Entries.Clear();
        }

        private sealed class PartialResultEntry
        {
            public PartialResultEntry(int generation, string value)
            {
                Generation = generation;
                Value = value;
            }

            public int Generation { get; }

            public string Value { get; }
        }
    }
}
