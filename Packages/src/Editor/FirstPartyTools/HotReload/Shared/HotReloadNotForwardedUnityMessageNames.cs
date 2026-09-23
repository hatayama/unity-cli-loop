// This file is compiled twice: into the Unity editor assembly (host side) and into the
// out-of-process transform worker (see TransformWorkerBootstrap.CollectWorkerSourcePaths).
// It must therefore stay free of Unity, Newtonsoft and Roslyn references.
using System;
using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// The Unity messages hot reload never forwards to an added method: the engine calls them
    /// only after 'uloop compile'. Shared so the worker's guidance and the Editor's proxy agree
    /// on the list.
    /// </summary>
    internal static class HotReloadNotForwardedUnityMessageNames
    {
        // Why these stay out: the first four are lifecycle messages whose proxy timing does not
        // match the target's own (a proxy is attached and destroyed on its own schedule, so its
        // Awake/OnEnable/OnDisable/OnDestroy would fire at moments the target never sees), and
        // the last four are editor-only messages that run outside Play Mode, where no proxy exists.
        private static readonly HashSet<string> Names = new HashSet<string>(StringComparer.Ordinal)
        {
            "Awake",
            "OnEnable",
            "OnDisable",
            "OnDestroy",
            "Reset",
            "OnValidate",
            "OnDrawGizmos",
            "OnDrawGizmosSelected"
        };

        internal static bool Contains(string methodName)
        {
            return methodName != null && Names.Contains(methodName);
        }
    }
}
