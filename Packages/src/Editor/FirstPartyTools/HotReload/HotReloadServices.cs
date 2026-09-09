using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Everything the hot-reload tool needs wired together for one Unity domain. The composition
    /// root builds it; callers take what they need from it rather than reaching for a static.
    /// </summary>
    internal sealed class HotReloadServices
    {
        internal HotReloadServices(
            HotReloadDomain domain,
            IHotReloadHarmony harmony,
            HotReloadPatcher patcher,
            HotReloadFileEntryApplier fileEntryApplier,
            HotReloadEntryApplier entryApplier)
        {
            Debug.Assert(domain != null, "domain must not be null.");
            Debug.Assert(harmony != null, "harmony must not be null.");
            Debug.Assert(patcher != null, "patcher must not be null.");
            Debug.Assert(fileEntryApplier != null, "fileEntryApplier must not be null.");
            Debug.Assert(entryApplier != null, "entryApplier must not be null.");
            Domain = domain;
            Harmony = harmony;
            Patcher = patcher;
            FileEntryApplier = fileEntryApplier;
            EntryApplier = entryApplier;
        }

        internal HotReloadDomain Domain { get; }

        internal IHotReloadHarmony Harmony { get; }

        internal HotReloadPatcher Patcher { get; }

        internal HotReloadFileEntryApplier FileEntryApplier { get; }

        internal HotReloadEntryApplier EntryApplier { get; }
    }
}
