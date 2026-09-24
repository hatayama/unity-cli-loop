namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// The Unity side of wired-value persistence: naming hosts and values so they can be found
    /// again after a scene reload, and finding them. Kept behind an interface so the ledger logic
    /// is testable without scenes.
    /// </summary>
    internal interface IHotReloadWiredValueResolver
    {
        /// <summary>
        /// Whether the caller is on the editor main thread, where scenes and assets can be read.
        /// </summary>
        bool IsMainThread { get; }

        /// <summary>
        /// Identity of a host the ledger can find again after a scene reload; null when the host
        /// is not a scene object or asset, or the call is off the main thread.
        /// </summary>
        string DescribeHost(object host);

        /// <summary>
        /// Whether the host recorded under this identity is no longer at its place: its scene can
        /// be read but nothing of that component type sits there. False for an asset host, a scene
        /// that is not loaded, or a call off the main thread.
        /// </summary>
        bool IsHostMissing(string hostIdentity);

        /// <summary>
        /// How to remember a value: plain values pass through, scene objects and assets become
        /// identities, anything else becomes unrestorable with a reason.
        /// </summary>
        HotReloadWiredValueDescriptor DescribeValue(object value);

        /// <summary>
        /// Turns a SceneObject or Asset descriptor back into a live object. False, with a
        /// one-sentence reason, when nothing matches.
        /// </summary>
        bool TryResolve(HotReloadWiredValueDescriptor descriptor, out object value, out string failureReason);
    }
}
