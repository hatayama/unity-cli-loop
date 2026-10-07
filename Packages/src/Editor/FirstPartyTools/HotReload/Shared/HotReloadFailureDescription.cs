using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// A hot-reload failure's message together with the kinds of failure it reports, so the
    /// response chooses its next step from what failed rather than from the message text.
    /// </summary>
    internal sealed class HotReloadFailureDescription
    {
        private HotReloadFailureDescription(string message, HotReloadFailureKinds kinds)
        {
            Debug.Assert(!string.IsNullOrEmpty(message), "message must not be null or empty.");
            Debug.Assert(kinds != HotReloadFailureKinds.None, "A failure must have at least one kind.");
            Message = message;
            Kinds = kinds;
        }

        internal string Message { get; }

        internal HotReloadFailureKinds Kinds { get; }

        /// <summary>A failure the reader fixes in the source or the request.</summary>
        internal static HotReloadFailureDescription Declaration(string message)
        {
            return new HotReloadFailureDescription(message, HotReloadFailureKinds.Declaration);
        }

        /// <summary>A failure caused only by the Editor compiling or importing during the run.</summary>
        internal static HotReloadFailureDescription EditorNotReady(string message)
        {
            return new HotReloadFailureDescription(message, HotReloadFailureKinds.EditorNotReady);
        }

        /// <summary>
        /// A failure caused by a missing compiled assembly. A Virtual Player also gets
        /// <see cref="HotReloadFailureKinds.VirtualPlayer"/>, because a compile in its own project
        /// cannot produce the assembly.
        /// </summary>
        internal static HotReloadFailureDescription CompiledAssemblyMissing(string message, bool isVirtualPlayer)
        {
            HotReloadFailureKinds kinds = isVirtualPlayer
                ? HotReloadFailureKinds.CompiledAssemblyMissing | HotReloadFailureKinds.VirtualPlayer
                : HotReloadFailureKinds.CompiledAssemblyMissing;
            return new HotReloadFailureDescription(message, kinds);
        }

        /// <summary>The same failure, with <paramref name="prefix"/> put before its message.</summary>
        internal HotReloadFailureDescription WithMessagePrefix(string prefix)
        {
            Debug.Assert(!string.IsNullOrEmpty(prefix), "prefix must not be null or empty.");
            return new HotReloadFailureDescription(prefix + Message, Kinds);
        }
    }
}
