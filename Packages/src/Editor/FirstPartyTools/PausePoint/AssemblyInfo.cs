using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("UnityCLILoop.Tests.Editor.SourcePausePointResolver")]
[assembly: InternalsVisibleTo("UnityCLILoop.Tests.Editor.SourcePausePointCapture")]
[assembly: InternalsVisibleTo("UnityCLILoop.Tests.Editor.SourcePausePointPatcher")]
// PausePointTests (Tests.Editor) exercises the Resolver/Patcher pipeline end-to-end and needs
// SourcePausePointPatcher visibility to prove Unpatch actually detaches the ledger entry.
[assembly: InternalsVisibleTo("UnityCLILoop.Tests.Editor")]
