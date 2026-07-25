# Code Complexity Checks

This repository measures cyclomatic complexity for both implementation stacks:

- Go native CLI code uses `golangci-lint` with the `cyclop` linter.
- Unity package C# code uses Microsoft's CA1502 analyzer through the local `UnityCliLoop.CodeComplexity` host.

The first rollout is advisory. The check reports findings but does not fail CI unless `CODE_COMPLEXITY_FAIL_ON_EXCEEDED=true` is set.

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

The repository-wide maximum cyclomatic complexity is 15. It is declared in two places that must
stay in step: `MAX_COMPLEXITY` in `scripts/check-code-complexity.sh` and `cyclop.max-complexity`
in `cli/.golangci-complexity.yml`. The `Code Complexity` workflow runs on every pull request that
touches `cli/**/*.go` or `Packages/src/**/*.cs`, reports findings, and uploads per-module JSON
artifacts — it never fails the build.

When touching a reported function, prefer reducing complexity before adding behavior. Use tests first, then refactor with small steps such as guard clauses, Extract Method, or data-driven dispatch.

Because the check is advisory, the threshold only does its job if someone reads the report. Treat
a new finding in code you are already editing as review feedback, not as noise to defer.
