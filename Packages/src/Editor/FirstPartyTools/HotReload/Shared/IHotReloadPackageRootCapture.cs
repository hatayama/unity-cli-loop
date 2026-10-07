using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Captures the package folder mapping on the Unity main thread so path normalization can run
    /// on the background threads the hot-reload run switches to.
    /// </summary>
    internal interface IHotReloadPackageRootCapture
    {
        /// <summary>Refreshes the mapping. Must be called from the Unity main thread.</summary>
        void CaptureCurrent();

        IReadOnlyList<ScriptPackageRoot> Current { get; }
    }
}
