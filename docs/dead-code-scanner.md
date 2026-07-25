# Dead code scanner

Use the C# dead-code scanner before deleting apparently unreferenced C# code or
before adding comments to explain why an apparently unreferenced type must stay.

For type-level review, especially when checking classes that may be kept by
Unity, serialization, reflection, release automation, or external package APIs,
run:

```bash
dotnet run --project tools/UnityCliLoop.DeadCodeScanner -- --scope public --include-types true --include-members false --include-locals false --include-test-only true --include-kept true --format table
```

For a broader member/local-variable pass, run:

```bash
dotnet run --project tools/UnityCliLoop.DeadCodeScanner -- --scope public --include-types true --include-members true --include-locals true --include-test-only true --include-kept false --format table
```

## CI gate

`.github/workflows/dead-code.yml` runs automatically on pull requests that
target `main` or `v3-beta` and touch `Packages/src/**/*.cs`, the scanner
itself, its tests, `scripts/check-dead-code.sh`, or the workflow file.

The gate uses `--fail-on high-confidence`, so CI fails only for
`Unused`, `UnusedPrivateMember`, and `UnusedLocal`.

`PublicCandidate` and `TestOnly` do not fail CI. Those findings need
manual review of non-C# references and cannot be decided mechanically.

## Interpreting the output

Interpret scanner output conservatively:

- `KeptByUnityOrReflection` usually means the symbol is intentionally reachable through Unity callbacks, attributes, serialization, or reflection-style discovery. Do not add explanatory comments for every such symbol when the attribute/base type already makes the reason obvious.
- `PublicCandidate` means Roslyn found no direct references. Check non-C# references such as `release-please-config.json`, checked-in JSON contracts, Unity assets, generated files, and documented public APIs before removing or commenting the symbol.
- If a symbol is referenced only by non-C# tooling, verify that the tool reads it for runtime or release behavior. If the tool only rewrites the symbol and no code reads it, remove the marker instead of documenting it.

## Intentionally retained PublicCandidates

These symbols stay `PublicCandidate` on purpose. Do not delete them during routine triage,
and do not introduce a `[UnityCliLoopKeep]` attribute just to silence the scanner.

| Symbol | Why it stays |
|---|---|
| `UnityCliLoopToolRegistrar.RegisterCustomTool` | Public extension API for external packages that register custom tools. Missing in-repo callers is expected. |
| `UnityCliLoopToolRegistrar.UnregisterCustomTool` | Same public extension API. |
| `UnityCliLoopToolRegistrar.GetRegisteredCustomTools` | Same public extension API. |
| `UnityCliLoopToolRegistrar.IsCustomToolRegistered` | Same public extension API. |
| `UnityCliLoopToolRegistrar.GetDebugInfo` | Same public extension API. |
| `UnityCliLoopToolRegistrar.NotifyToolChanges` | Same public extension API. |
| `ExecuteDynamicCodeResponse.Error` | Documented tool response field (`ExecuteDynamicCode` Skill). Outbound JSON shape, not an in-repo read. |
