# Vendored Lib.Harmony DLL: Provenance and Rebuild Steps

`UnityCliLoop.0Harmony.dll` is a private renamed copy of the Lib.Harmony
(Harmony 2) assembly, vendored the same way as the Roslyn dependency DLLs
under `Packages/src/Editor/FirstPartyTools/ExecuteDynamicCode/Plugins/CodeAnalysis/`
(see PR #1364 in this repository's history for that precedent).

## Source

- NuGet package: `Lib.Harmony`
- Package version: `2.4.2`
- Package asset used: `lib/net472/0Harmony.dll`
- Source URL: https://api.nuget.org/v3-flatcontainer/lib.harmony/2.4.2/lib.harmony.2.4.2.nupkg
- Upstream project: https://github.com/pardeike/Harmony
- License: MIT (see `LICENSE.md` in this directory)

## Checksums

| File | SHA-256 |
| --- | --- |
| `lib.harmony.2.4.2.nupkg` (downloaded package) | `d64592e53090464559fce48612c9ca7c8dc73113841376b7aa3455f46fc5d579` |
| `lib/net472/0Harmony.dll` (original, before rename) | `7b9e756306fa3d7620e02a857c8927a6ab04973f9bd8a77d3866700a6deac55c` |
| `UnityCliLoop.0Harmony.dll` (renamed, as vendored here) | `cf9ad14a6dbc061f8b75dd0f17a3e2fdd427af5c746d243fcb39e7a6f6c5c039` |

## Why `net472`

The Lib.Harmony package ships separate builds per target framework
(`net35`, `net452`, `net472`, `net48`, `netcoreapp3.x`, `net5.0`+, ...). Unity
Editor scripting on Mono uses a .NET Framework-compatible surface, so
`net472` was picked as the closest match — the same reasoning used for the
existing `net462` picks under `ExecuteDynamicCode/Plugins/CodeAnalysis/`. The
`.NETFramework4.7.2` dependency group in the nuspec has no extra NuGet
dependencies, confirming this is Harmony's "fat" build with MonoMod merged in
(no separate MonoMod assembly to vendor).

## Why the assembly was renamed

Harmony instances hosted inside the Unity Editor process must not collide
with a different `0Harmony.dll` a user's own project or another package may
already load (the same identity-clash concern that motivated renaming the
`System.*` assemblies under `ExecuteDynamicCode/Plugins/CodeAnalysis/`). Only
the assembly identity (the `AssemblyName` and the module name) was changed;
namespaces and type names (`HarmonyLib.*`, `MonoMod.*`) are untouched, so the
public Harmony API surface is unaffected. The original assembly was already
unsigned (zero-sized public key), so no strong-name stripping was needed.

## Rebuild procedure

1. Download the nupkg:
   `curl -sL -o lib.harmony.2.4.2.nupkg https://api.nuget.org/v3-flatcontainer/lib.harmony/2.4.2/lib.harmony.2.4.2.nupkg`
2. Extract `lib/net472/0Harmony.dll` from the nupkg (it is a zip archive).
3. Rewrite the assembly identity with Mono.Cecil (`AssemblyDefinition.Name.Name`
   and `MainModule.Name`) from `0Harmony` to `UnityCliLoop.0Harmony`, then
   write the result out as `UnityCliLoop.0Harmony.dll`. No other bytes are
   modified; namespaces, types, and IL are untouched. There is a single
   assembly here (a fat build), so there are no cross-assembly `AssemblyRef`
   entries that also need renaming.
4. Verify the rename with `monodis --assembly UnityCliLoop.0Harmony.dll`
   (expect `Name: UnityCliLoop.0Harmony`) and confirm the original namespaces
   are intact (e.g. `strings UnityCliLoop.0Harmony.dll | grep '^HarmonyLib'`).
5. Place the renamed DLL next to a `.meta` with `isExplicitlyReferenced: 1`
   and Editor-only platform data (see `UnityCliLoop.0Harmony.dll.meta` in
   this directory), and reference it from the consuming asmdef via
   `overrideReferences: true` + `precompiledReferences`.
