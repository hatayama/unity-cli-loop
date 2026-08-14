using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("UnityCLILoop.FirstPartyTools.PausePoint.Editor")]
// HotReloadTools formats retarget warnings from registry ResolvedLine / ResolvedLineText.
[assembly: InternalsVisibleTo("UnityCLILoop.FirstPartyTools.HotReload.Editor")]
[assembly: InternalsVisibleTo("UnityCLILoop.FirstPartyTools.RunTests.Editor")]
[assembly: InternalsVisibleTo("UnityCLILoop.FirstPartyTools.SimulateKeyboard.Editor")]
[assembly: InternalsVisibleTo("UnityCLILoop.FirstPartyTools.SimulateMouseInput.Editor")]
[assembly: InternalsVisibleTo("UnityCLILoop.FirstPartyTools.SimulateMouseUi.Editor")]
[assembly: InternalsVisibleTo("UnityCLILoop.FirstPartyTools.ExecuteDynamicCode.Editor")]
[assembly: InternalsVisibleTo("UnityCLILoop.FirstPartyTools.Common.Preflight.Editor")]
[assembly: InternalsVisibleTo("UnityCLILoop.FirstPartyTools.Compile.Editor")]
[assembly: InternalsVisibleTo("UnityCLILoop.FirstPartyTools.ControlPlayMode.Editor")]
[assembly: InternalsVisibleTo("UnityCLILoop.Infrastructure")]
[assembly: InternalsVisibleTo("UnityCLILoop.Tests.Editor")]
[assembly: InternalsVisibleTo("UnityCLILoop.Tests.PlayMode")]
[assembly: InternalsVisibleTo("UnityCLILoop.Tests.Editor.SourcePausePointCapture")]
[assembly: InternalsVisibleTo("UnityCLILoop.Tests.Editor.SourcePausePointPatcher")]
// The hot-reload contract tests patch a fixture with HotReloadPatcher and then drive SourcePausePointPatcher against it.
[assembly: InternalsVisibleTo("UnityCLILoop.Tests.Editor.HotReload")]
