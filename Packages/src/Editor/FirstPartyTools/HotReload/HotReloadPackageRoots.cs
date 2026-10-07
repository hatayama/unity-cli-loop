using System;
using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// The production capture: reads the packages Unity has registered, and holds the last
    /// mapping it read.
    /// </summary>
    internal sealed class HotReloadPackageRootCapture : IHotReloadPackageRootCapture
    {
        private IReadOnlyList<ScriptPackageRoot> _current;

        public void CaptureCurrent()
        {
            _current = ScriptPackageRoots.ReadCurrent();
        }

        public IReadOnlyList<ScriptPackageRoot> Current
        {
            get
            {
                // Fail fast rather than capturing lazily: PackageInfo is main-thread only, so a
                // lazy capture would succeed or throw depending on which thread first asked, and a
                // caller that never passed through the run entry point would be silently tolerated.
                if (_current == null)
                {
                    throw new InvalidOperationException(
                        "Hot-reload package roots were never captured. Call "
                        + nameof(HotReloadPackageRootCapture) + "." + nameof(CaptureCurrent)
                        + " on the Unity main thread before normalizing script paths.");
                }

                return _current;
            }
        }
    }
}
