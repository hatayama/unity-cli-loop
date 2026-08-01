using System.Runtime.CompilerServices;

// Spike / production EditMode tests live in the dedicated HotReload test asmdef and need
// visibility into the publicizer / matcher / patcher internals.
[assembly: InternalsVisibleTo("UnityCLILoop.Tests.Editor.HotReload")]
