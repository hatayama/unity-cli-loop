using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("UnityCLILoop.FirstPartyTools.Editor")]
[assembly: InternalsVisibleTo("UnityCLILoop.FirstPartyTools.Watch.Editor")]
// Hot reload compiles shims through RoslynCompilerBackend / ExternalCompilerPathResolver
// (both internal) and loads them via CompiledAssemblyLoader.
[assembly: InternalsVisibleTo("UnityCLILoop.FirstPartyTools.HotReload.Editor")]
// The hot-reload shared kernel resolves the external compiler paths for the transform worker.
[assembly: InternalsVisibleTo("UnityCLILoop.FirstPartyTools.HotReload.Shared.Editor")]
// The introduced-type and patching aggregates compile through the same internal backend.
[assembly: InternalsVisibleTo("UnityCLILoop.FirstPartyTools.HotReload.IntroducedType.Editor")]
[assembly: InternalsVisibleTo("UnityCLILoop.FirstPartyTools.HotReload.Patching.Editor")]
[assembly: InternalsVisibleTo("UnityCLILoop.Tests.Editor")]
[assembly: InternalsVisibleTo("UnityCLILoop.Tests.Editor.HotReload")]
[assembly: InternalsVisibleTo("UnityCLILoop.Tests.PlayMode")]
[assembly: InternalsVisibleTo("UnityCLILoop.Tests.Demo.Editor")]
