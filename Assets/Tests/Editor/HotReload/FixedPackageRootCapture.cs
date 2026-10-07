using System.Collections.Generic;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// A package root capture that always holds the roots it was built with, for tests that build
    /// a collaborator reading captured roots without asking the Package Manager.
    /// </summary>
    internal sealed class FixedPackageRootCapture : IHotReloadPackageRootCapture
    {
        private readonly IReadOnlyList<ScriptPackageRoot> _roots;

        internal FixedPackageRootCapture(IReadOnlyList<ScriptPackageRoot> roots)
        {
            _roots = roots;
        }

        public void CaptureCurrent()
        {
        }

        public IReadOnlyList<ScriptPackageRoot> Current => _roots;
    }
}
