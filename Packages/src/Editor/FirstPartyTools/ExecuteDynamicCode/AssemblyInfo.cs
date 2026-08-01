using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("UnityCLILoop.FirstPartyTools.Editor")]
[assembly: InternalsVisibleTo("UnityCLILoop.FirstPartyTools.Watch.Editor")]
// Hot reload compiles shims through RoslynCompilerBackend / ExternalCompilerPathResolver
// (both internal) and loads them via CompiledAssemblyLoader.
[assembly: InternalsVisibleTo("UnityCLILoop.FirstPartyTools.HotReload.Editor")]
[assembly: InternalsVisibleTo("UnityCLILoop.Tests.Editor")]
[assembly: InternalsVisibleTo("UnityCLILoop.Tests.Editor.HotReload")]
[assembly: InternalsVisibleTo("UnityCLILoop.Tests.PlayMode")]
[assembly: InternalsVisibleTo("UnityCLILoop.Tests.Demo.Editor")]
