using System.Collections.Concurrent;
using System.Threading;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Counts how many times each hot-reload patched method body has run since it was applied.
    /// Keys match <see cref="HotReloadPatcher"/> status labels so --status can report counts.
    /// </summary>
    internal static class HotReloadInvocationRegistry
    {
        private sealed class Counter
        {
            public long Value;
        }

        private static readonly ConcurrentDictionary<string, Counter> CountsByMethodKey =
            new ConcurrentDictionary<string, Counter>();

        /// <summary>
        /// Increments the counter for <paramref name="methodKey"/>. Called from patched IL on
        /// every invocation; Interlocked keeps the hot path safe under concurrent callers.
        /// </summary>
        public static void Increment(string methodKey)
        {
            if (string.IsNullOrEmpty(methodKey))
            {
                return;
            }

            Counter counter = CountsByMethodKey.GetOrAdd(methodKey, _ => new Counter());
            Interlocked.Increment(ref counter.Value);
        }

        /// <summary>
        /// Returns the invocation count for <paramref name="methodKey"/>, or 0 when unknown.
        /// </summary>
        public static long GetCount(string methodKey)
        {
            if (string.IsNullOrEmpty(methodKey))
            {
                return 0L;
            }

            if (!CountsByMethodKey.TryGetValue(methodKey, out Counter counter))
            {
                return 0L;
            }

            return Interlocked.Read(ref counter.Value);
        }

        /// <summary>
        /// Drops the counter for one method key (unpatch / re-apply of that method).
        /// </summary>
        public static void Remove(string methodKey)
        {
            if (string.IsNullOrEmpty(methodKey))
            {
                return;
            }

            CountsByMethodKey.TryRemove(methodKey, out _);
        }

        /// <summary>
        /// Clears every counter (--revert-all / domain teardown of all patches).
        /// </summary>
        public static void Clear()
        {
            CountsByMethodKey.Clear();
        }
    }
}
