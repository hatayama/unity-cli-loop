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
dotnet run --project tools/UnityCliLoop.CodeComplexity -- --root . --max-complexity 25
```

## Operating Policy

Start with a maximum cyclomatic complexity of 25. This matches the default threshold for CA1502 and avoids turning existing hotspots into immediate PR blockers.

When touching a reported function, prefer reducing complexity before adding behavior. Use tests first, then refactor with small steps such as guard clauses, Extract Method, or data-driven dispatch.

Do not lower the repository-wide threshold until the current warning list is small enough that the stricter threshold creates actionable review feedback.
