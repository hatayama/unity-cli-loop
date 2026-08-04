using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("UnityCLILoop.FirstPartyTools.Editor")]
// Watch reuses the captured-variable preview serializer so get-watch-values matches CapturedVariables.
[assembly: InternalsVisibleTo("UnityCLILoop.FirstPartyTools.Watch.Editor")]
[assembly: InternalsVisibleTo("UnityCLILoop.Tests.Editor.SourcePausePointResolver")]
[assembly: InternalsVisibleTo("UnityCLILoop.Tests.Editor.SourcePausePointCapture")]
[assembly: InternalsVisibleTo("UnityCLILoop.Tests.Editor.SourcePausePointPatcher")]
// PausePointTests (Tests.Editor) exercises the Resolver/Patcher pipeline end-to-end and needs
// SourcePausePointPatcher visibility to prove Unpatch actually detaches the ledger entry.
[assembly: InternalsVisibleTo("UnityCLILoop.Tests.Editor")]
// The hot-reload contract tests patch a fixture with HotReloadPatcher and then drive SourcePausePointPatcher against it.
[assembly: InternalsVisibleTo("UnityCLILoop.Tests.Editor.HotReload")]
