namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// What a run tells the caller about an added Unity message: the note on the method's own row,
    /// and the one line that names the messages the engine will not reach until a compile.
    /// </summary>
    internal static class HotReloadUnityMessageNotes
    {
        /// <summary>
        /// The note for a message a proxy delivers: what carries the call and how long it lasts.
        /// </summary>
        internal const string Forwarded =
            "Unity message: forwarded to live instances by a hot-reload proxy component while Play "
            + "Mode runs. The proxy is rebuilt only when a later reload changes which messages this "
            + "type adds or their signatures. Execution order relative to other components is not "
            + "guaranteed. Gone on any compile or domain reload.";

        /// <summary>
        /// The note for an added Start. It is the only forwarded message that runs because the
        /// proxy attaches, so only its row says what happens on instances that already exist.
        /// </summary>
        internal const string ForwardedStart =
            "Unity message: forwarded to live instances by a hot-reload proxy component while Play "
            + "Mode runs. The added Start runs once on each existing instance when the proxy "
            + "attaches. The proxy is rebuilt (and the added Start runs again) only when a later "
            + "reload changes which messages this type adds or their signatures. Execution order "
            + "relative to other components is not guaranteed. Gone on any compile or domain "
            + "reload.";

        /// <summary>
        /// The note for a message the engine dispatches but this feature leaves to the compiler.
        /// </summary>
        internal const string NotForwarded =
            "Unity message: not invoked by the engine until 'uloop compile'; "
            + "Awake/OnEnable/OnDisable/OnDestroy, editor-only messages, and non-void messages are "
            + "not forwarded by hot reload.";

        /// <summary>
        /// The run-level line that names every not-forwarded message of the run at once, so a
        /// caller reading Warnings alone still learns a compile is needed.
        /// </summary>
        internal const string NotForwardedWarningFormat =
            "Added Unity messages not invoked until 'uloop compile': {0}";

        /// <summary>Whether a row's note says a proxy delivers the message.</summary>
        internal static bool IsForwarded(string lifecycleNote)
        {
            return lifecycleNote == Forwarded || lifecycleNote == ForwardedStart;
        }
    }
}
