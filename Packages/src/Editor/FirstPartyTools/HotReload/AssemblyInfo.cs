using System.Runtime.CompilerServices;

// Spike / production EditMode tests live in the dedicated HotReload test asmdef and need
// visibility into the publicizer / matcher / patcher internals.
[assembly: InternalsVisibleTo("UnityCLILoop.Tests.Editor.HotReload")]

// Why: CompositionRoot's asmdef does not reference HotReload, so domain-reload snapshot
// capture is registered through the FirstPartyTools.Editor facade (same pattern as PausePoint).
[assembly: InternalsVisibleTo("UnityCLILoop.FirstPartyTools.Editor")]
