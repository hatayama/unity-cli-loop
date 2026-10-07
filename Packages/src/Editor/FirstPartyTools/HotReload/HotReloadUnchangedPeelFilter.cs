using System;
using System.Collections.Generic;
using System.Reflection;

using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Tells which of a run's unchanged rows can still hold a patch of an earlier reload, so the
    /// peel resolves those rows only.
    /// </summary>
    internal static class HotReloadUnchangedPeelFilter
    {
        /// <summary>The names of every method that holds a live patch, whatever file patched it.</summary>
        internal static HashSet<string> CollectPatchedMethodNames(IReadOnlyList<HotReloadFileGeneration> generations)
        {
            Debug.Assert(generations != null, "generations must not be null.");
            HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);
            foreach (HotReloadFileGeneration generation in generations)
            {
                foreach (MethodBase method in generation.ListActiveMethods())
                {
                    names.Add(method.Name);
                }
            }

            return names;
        }
    }
}
