namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// The fixed entry point patched method bodies reach the current domain's invocation counts
    /// through.
    /// </summary>
    /// <remarks>
    /// Why a static gateway rather than an injected value: the caller is emitted IL inside a
    /// patched body, which can only call a static method. The counts themselves live in
    /// <see cref="HotReloadInvocationCounts"/>, owned by the domain and installed here by the
    /// composition root.
    /// </remarks>
    internal static class HotReloadInvocationRegistry
    {
        /// <summary>
        /// The counts of the domain currently installed, or null while none is. Set by the
        /// composition root only.
        /// </summary>
        internal static HotReloadInvocationCounts Current { get; set; }

        /// <summary>
        /// Increments the counter for <paramref name="methodKey"/>. Called from patched IL on
        /// every invocation, including from threads other than the editor's main thread.
        /// </summary>
        public static void Increment(string methodKey)
        {
            // Why the slot is read once into a local: a patched body can run while the composition
            // root swaps domains, and reading twice could increment one set and clear another.
            // A count lost to that swap is accepted: the patch it counted is being torn down.
            HotReloadInvocationCounts counts = Current;
            counts?.Increment(methodKey);
        }

        /// <summary>
        /// Returns the invocation count for <paramref name="methodKey"/>, or 0 when unknown.
        /// </summary>
        public static long GetCount(string methodKey)
        {
            HotReloadInvocationCounts counts = Current;
            return counts == null ? 0L : counts.GetCount(methodKey);
        }

        /// <summary>
        /// Drops the counter for one method key (unpatch / re-apply of that method).
        /// </summary>
        public static void Remove(string methodKey)
        {
            HotReloadInvocationCounts counts = Current;
            counts?.Remove(methodKey);
        }

        /// <summary>
        /// Clears every counter (--revert-all / domain teardown of all patches).
        /// </summary>
        public static void Clear()
        {
            HotReloadInvocationCounts counts = Current;
            counts?.Clear();
        }
    }
}
