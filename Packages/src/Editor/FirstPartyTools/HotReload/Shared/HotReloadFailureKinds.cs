using System;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// The kinds of failure a hot-reload run reported, so the next step it recommends can tell a
    /// failure the reader has to fix from one that passes once the Editor settles or a compile runs.
    /// </summary>
    [Flags]
    internal enum HotReloadFailureKinds
    {
        None = 0,

        /// <summary>
        /// Something in the source or the request needs a change, or the failure has no more
        /// specific kind. Every failure reported before these kinds existed is this one.
        /// </summary>
        Declaration = 1,

        /// <summary>
        /// The Editor compiled or imported while the run was underway, so nothing in the source
        /// needs a change.
        /// </summary>
        EditorNotReady = 2,

        /// <summary>The compiled assembly for the file is missing, and a compile produces it.</summary>
        CompiledAssemblyMissing = 4,

        /// <summary>
        /// The Editor is a Multiplayer Play Mode Virtual Player, which has no compiled assembly of
        /// its own. Always set together with <see cref="CompiledAssemblyMissing"/>.
        /// </summary>
        VirtualPlayer = 8
    }
}
