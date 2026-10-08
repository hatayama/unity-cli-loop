using System;
using System.Collections.Generic;

using UnityEngine;

using UnityCompilationAssembly = UnityEditor.Compilation.Assembly;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Keeps the last non-empty compilation assembly list so repeated lookups ask Unity only once
    /// until the owner invalidates it. Main thread only, like the Unity call it wraps.
    /// </summary>
    internal sealed class HotReloadCompilationAssemblyCache
    {
        private readonly Func<UnityCompilationAssembly[]> _fetch;
        private UnityCompilationAssembly[] _assemblies;

        internal HotReloadCompilationAssemblyCache(Func<UnityCompilationAssembly[]> fetch)
        {
            Debug.Assert(fetch != null, "fetch must not be null.");
            _fetch = fetch;
        }

        internal bool IsFilled => _assemblies != null;

        /// <summary>
        /// Returns the memoized list, asking Unity when nothing is kept. The list is shared; callers only read it.
        /// </summary>
        internal IReadOnlyList<UnityCompilationAssembly> Current()
        {
            if (_assemblies != null)
            {
                return _assemblies;
            }

            UnityCompilationAssembly[] fetched = _fetch();
            Debug.Assert(fetched != null, "fetch must not return null.");

            // Why not memoize an empty result: GetAssemblies() answers nothing while a compile is in
            // flight, and keeping that would hide every assembly for the rest of the domain's life.
            if (fetched.Length > 0)
            {
                _assemblies = fetched;
            }

            return fetched;
        }

        /// <summary>
        /// Returns the first compilation assembly with this name, or null when none has it.
        /// </summary>
        internal UnityCompilationAssembly FindByName(string assemblyName)
        {
            Debug.Assert(!string.IsNullOrEmpty(assemblyName), "assemblyName must not be null or empty.");

            IReadOnlyList<UnityCompilationAssembly> assemblies = Current();
            for (int index = 0; index < assemblies.Count; index++)
            {
                if (assemblies[index].name == assemblyName)
                {
                    return assemblies[index];
                }
            }

            return null;
        }

        internal void Invalidate()
        {
            _assemblies = null;
        }
    }
}
