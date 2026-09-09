using System.Runtime.CompilerServices;

// The patching aggregate reads introduced-type artifacts.
[assembly: InternalsVisibleTo("UnityCLILoop.FirstPartyTools.HotReload.Patching.Editor")]

// The application assembly composes this part; the split is internal to the hot-reload tool.
[assembly: InternalsVisibleTo("UnityCLILoop.FirstPartyTools.HotReload.Editor")]

// EditMode tests drive these internals directly.
[assembly: InternalsVisibleTo("UnityCLILoop.Tests.Editor.HotReload")]
