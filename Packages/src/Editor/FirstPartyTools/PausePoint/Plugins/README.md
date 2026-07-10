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
2. Verify the download against the checksum table above before doing anything
   else with it, and stop if it does not match:
   `shasum -a 256 lib.harmony.2.4.2.nupkg` (expect
   `d64592e53090464559fce48612c9ca7c8dc73113841376b7aa3455f46fc5d579`).
3. Extract `lib/net472/0Harmony.dll` from the nupkg (it is a zip archive) and
   verify it against the checksum table above the same way (expect
   `7b9e756306fa3d7620e02a857c8927a6ab04973f9bd8a77d3866700a6deac55c`).
4. Rewrite the assembly identity with Mono.Cecil 0.11.5 (the same version
   `com.unity.nuget.mono-cecil` 1.11.6 wraps) via a small console script:

   ```csharp
   // dotnet add package Mono.Cecil --version 0.11.5
   using Mono.Cecil;

   var readerParams = new ReaderParameters { ReadSymbols = false };
   using var assembly = AssemblyDefinition.ReadAssembly(inputPath, readerParams);

   assembly.Name.Name = "UnityCliLoop.0Harmony";
   assembly.MainModule.Name = "UnityCliLoop.0Harmony.dll";

   assembly.Write(outputPath);
   ```

   This changes only the assembly's `AssemblyName` and module name;
   namespaces, type names, IL, and `AssemblyRef` entries are untouched. Note
   that the underlying PE/metadata bytes at those two fields necessarily
   differ from the original file as a result (that is the entire point of
   the rewrite) — the checksum in the table below is for the resulting file,
   not a claim that the file is otherwise byte-identical to the original.
   There is a single assembly here (a fat build), so there are no
   cross-assembly `AssemblyRef` entries pointing at other vendored DLLs that
   also need renaming.
5. Verify the rename with `monodis --assembly UnityCliLoop.0Harmony.dll`
   (expect `Name: UnityCliLoop.0Harmony`), confirm the original namespaces
   are intact (e.g. `strings UnityCliLoop.0Harmony.dll | grep '^HarmonyLib'`),
   and verify the result against the checksum table above:
   `shasum -a 256 UnityCliLoop.0Harmony.dll` (expect
   `cf9ad14a6dbc061f8b75dd0f17a3e2fdd427af5c746d243fcb39e7a6f6c5c039`).
6. Place the renamed DLL next to a `.meta` with `isExplicitlyReferenced: 1`
   and Editor-only platform data (see `UnityCliLoop.0Harmony.dll.meta` in
   this directory), and reference it from the consuming asmdef via
   `overrideReferences: true` + `precompiledReferences`.
