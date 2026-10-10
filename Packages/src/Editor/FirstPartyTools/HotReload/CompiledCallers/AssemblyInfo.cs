using System.Runtime.CompilerServices;

// Only the tests see these internals: the hot-reload application assembly uses this module
// through its entry point alone, and the compiler keeps it away from the parts behind it.
[assembly: InternalsVisibleTo("UnityCLILoop.Tests.Editor.HotReload")]
