using System.Collections.Concurrent;
using System.Threading;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// How many times each patched method body has run since it was applied, for one domain.
    /// Keys match the status labels so --status can report counts.
    /// </summary>
    internal sealed class HotReloadInvocationCounts
    {
        private sealed class Counter
        {
            public long Value;
        }

        private readonly ConcurrentDictionary<string, Counter> _countsByMethodKey =
            new ConcurrentDictionary<string, Counter>();

        /// <summary>
        /// Increments the counter for <paramref name="methodKey"/>. Called from patched IL on
        /// every invocation; Interlocked keeps the hot path safe under concurrent callers.
        /// </summary>
        internal void Increment(string methodKey)
        {
            if (string.IsNullOrEmpty(methodKey))
            {
                return;
            }

            Counter counter = _countsByMethodKey.GetOrAdd(methodKey, _ => new Counter());
            Interlocked.Increment(ref counter.Value);
        }

        /// <summary>
        /// Returns the invocation count for <paramref name="methodKey"/>, or 0 when unknown.
        /// </summary>
        internal long GetCount(string methodKey)
        {
            if (string.IsNullOrEmpty(methodKey))
            {
                return 0L;
            }

            if (!_countsByMethodKey.TryGetValue(methodKey, out Counter counter))
            {
                return 0L;
            }

            return Interlocked.Read(ref counter.Value);
        }

        /// <summary>
        /// Drops the counter for one method key (unpatch / re-apply of that method).
        /// </summary>
        internal void Remove(string methodKey)
        {
            if (string.IsNullOrEmpty(methodKey))
            {
                return;
            }

            _countsByMethodKey.TryRemove(methodKey, out _);
        }

        /// <summary>
        /// Clears every counter (--revert-all / domain teardown of all patches).
        /// </summary>
        internal void Clear()
        {
            _countsByMethodKey.Clear();
        }
    }
}
