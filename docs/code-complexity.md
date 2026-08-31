# Code Complexity Checks

This repository measures cyclomatic complexity for both implementation stacks:

- Go native CLI code uses `golangci-lint` with the `cyclop` linter.
- Unity package C# code uses Microsoft's CA1502 analyzer through the local `UnityCliLoop.CodeComplexity` host.

The `Code Complexity` workflow fails the pull request when cyclop or CA1502 findings exceed the
threshold of 15. Locally the check still reports in warning mode unless
`CODE_COMPLEXITY_FAIL_ON_EXCEEDED=true` is set.

## Local Usage

Run both checks:

```sh
scripts/check-code-complexity.sh
```

Use a different threshold:

```sh
CODE_COMPLEXITY_MAX_COMPLEXITY=20 scripts/check-code-complexity.sh
```

Fail on findings:

```sh
CODE_COMPLEXITY_FAIL_ON_EXCEEDED=true scripts/check-code-complexity.sh
```

Run the C# checker directly:

```sh
dotnet run --project tools/UnityCliLoop.CodeComplexity -- --root . --max-complexity 15
```

## Operating Policy

The repository-wide maximum cyclomatic complexity is 15. It is declared in five places that must
stay in step:

1. `MAX_COMPLEXITY` in `scripts/check-code-complexity.sh`
2. `cyclop.max-complexity` in `cli/.golangci-complexity.yml`
3. `--max-complexity` in the artifact step of `.github/workflows/code-complexity.yml`
4. `CA1502: 15` in `tools/UnityCliLoop.CodeComplexity/CodeMetricsConfig.txt` — this file-backed
   config is the one the C# host actually consumes whenever the requested threshold is 15
5. `maxComplexity: 15` in `CodeComplexityOptions.Default` — covers argument-less runs of the
   C# host only

The `Code Complexity` workflow runs on every pull request that
touches `cli/**/*.go` or `Packages/src/**/*.cs`, fails the job when findings exceed the threshold,
and uploads per-module JSON artifacts.

When touching a reported function, reduce its complexity before adding behavior. Use tests first, then refactor with small steps such as guard clauses, Extract Method, or data-driven dispatch.

A new finding in code you are already editing is a required fix, not noise to defer.
