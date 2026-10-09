using System.Collections.Generic;

using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Caps how many compiled assemblies one caller-note resolution may read into the call-site
    /// cache; the assemblies it refused are listed for the vibe log and for the backfill that
    /// reads them after the run.
    /// </summary>
    internal sealed class HotReloadCallSiteLoadBudget
    {
        private readonly List<string> _refusedAssemblyNames = new List<string>();
        private readonly List<string> _refusedDllPaths = new List<string>();

        public int RemainingLoads { get; private set; }

        /// <summary>
        /// Each assembly the budget refused, once, however many scans asked for it.
        /// </summary>
        public IReadOnlyList<string> RefusedAssemblyNames => _refusedAssemblyNames;

        /// <summary>
        /// The compiled dll of each assembly in <see cref="RefusedAssemblyNames"/>, in the same
        /// order; what the backfill after the run reads.
        /// </summary>
        public IReadOnlyList<string> RefusedDllPaths => _refusedDllPaths;

        public HotReloadCallSiteLoadBudget(int loads)
        {
            Debug.Assert(loads >= 0, "loads must not be negative.");
            RemainingLoads = loads;
        }

        /// <summary>
        /// Takes one load from the budget; false when none is left.
        /// </summary>
        public bool TryConsume()
        {
            if (RemainingLoads == 0)
            {
                return false;
            }

            RemainingLoads--;
            return true;
        }

        /// <summary>
        /// Records that a scan could not read <paramref name="assemblyName"/> because no load was
        /// left. Why deduplicated here: the direct scan and every closure level of one run may ask
        /// for the same assembly, and the log should name each refused dll once.
        /// </summary>
        public void Refuse(string assemblyName, string dllPath)
        {
            Debug.Assert(!string.IsNullOrEmpty(assemblyName), "assemblyName must not be null or empty.");
            Debug.Assert(!string.IsNullOrEmpty(dllPath), "dllPath must not be null or empty.");
            if (_refusedAssemblyNames.Contains(assemblyName))
            {
                return;
            }

            _refusedAssemblyNames.Add(assemblyName);
            _refusedDllPaths.Add(dllPath);
        }
    }
}
