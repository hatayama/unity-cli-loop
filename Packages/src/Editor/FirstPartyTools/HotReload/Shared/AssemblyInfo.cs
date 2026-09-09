using System.Runtime.CompilerServices;

// The patching aggregate builds on this shared kernel.
[assembly: InternalsVisibleTo("UnityCLILoop.FirstPartyTools.HotReload.Patching.Editor")]

// The introduced-type aggregate builds on this shared kernel.
[assembly: InternalsVisibleTo("UnityCLILoop.FirstPartyTools.HotReload.IntroducedType.Editor")]

// The application assembly composes this part; the split is internal to the hot-reload tool.
[assembly: InternalsVisibleTo("UnityCLILoop.FirstPartyTools.HotReload.Editor")]

// EditMode tests drive these internals directly.
[assembly: InternalsVisibleTo("UnityCLILoop.Tests.Editor.HotReload")]
