using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Everything the hot-reload tool needs wired together for one Unity domain. The composition
    /// root builds it; callers take what they need from it rather than reaching for a static.
    /// </summary>
    internal sealed class HotReloadServices
    {
        internal HotReloadServices(HotReloadDomain domain)
        {
            Debug.Assert(domain != null, "domain must not be null.");
            Domain = domain;
        }

        internal HotReloadDomain Domain { get; }
    }
}
