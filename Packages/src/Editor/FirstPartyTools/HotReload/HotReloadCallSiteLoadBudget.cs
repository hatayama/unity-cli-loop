using System.Collections.Generic;

using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Caps how many compiled assemblies one caller-note resolution may read into the call-site
    /// cache; the assemblies it refused are listed for the vibe log.
    /// </summary>
    internal sealed class HotReloadCallSiteLoadBudget
    {
        public int RemainingLoads { get; private set; }

        public List<string> RefusedAssemblyNames { get; }

        public HotReloadCallSiteLoadBudget(int loads)
        {
            Debug.Assert(loads >= 0, "loads must not be negative.");
            RemainingLoads = loads;
            RefusedAssemblyNames = new List<string>();
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
    }
}
