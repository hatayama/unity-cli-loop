using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("UnityCLILoop.Tests.Editor")]

// Why: CompositionRoot's asmdef does not reference Watch, so the post-domain-reload restore is
// registered through the FirstPartyTools.Editor facade (same pattern as HotReload and PausePoint).
[assembly: InternalsVisibleTo("UnityCLILoop.FirstPartyTools.Editor")]
