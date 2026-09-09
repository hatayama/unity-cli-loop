using System.Runtime.CompilerServices;

// The application assembly composes this part; the split is internal to the hot-reload tool.
[assembly: InternalsVisibleTo("UnityCLILoop.FirstPartyTools.HotReload.Editor")]

// Domain-reload snapshot capture is registered through the FirstPartyTools facade.
[assembly: InternalsVisibleTo("UnityCLILoop.FirstPartyTools.Editor")]

// EditMode tests drive these internals directly.
[assembly: InternalsVisibleTo("UnityCLILoop.Tests.Editor.HotReload")]

// Cross-assembly call-site tests drive these internals directly.
[assembly: InternalsVisibleTo("UnityCLILoop.Tests.Editor.HotReload.CallSiteCrossAssembly")]
