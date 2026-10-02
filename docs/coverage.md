# Test Coverage

The repository tracks statement coverage of the Go CLI modules and keeps it from falling.

## What is measured

- Every Go module under `cli/` (`common`, `dispatcher`, `project-runner`, `release-automation`),
  from `go test -coverprofile`.
- Blocks whose file path contains a pattern listed under `go.exclude` in
  `coverage-baseline.json` are left out. The defaults exclude `cmd/` entry points, which only
  parse flags and call into `internal/`, and `common/clitest`, which is a test helper.
- The figure is covered statements divided by all statements after exclusion, truncated to one
  decimal.

## The pull request gate

The `build-cli` job in `build-and-test.yml` runs `scripts/check-go-cli.sh` with
`GO_COVERAGE_DIR` set, which writes one profile per module, then runs
`cli/release-automation/cmd/coverage-report --mode gate`. The step fails when a module is more
than 0.1 point below its figure in `coverage-baseline.json`, and also when a module's profile
is missing, a profile names a module the baseline does not list, or a module has no statements
left after exclusion. The report reads every `<module>.out` in the coverage directory, so a
module newly added to `scripts/check-go-cli-source.sh` fails the gate until the baseline lists
it. The gate runs only on Linux, because build-tagged files make the figures
differ per OS.

## Raising the baseline

When a module is at least one point above its baseline, the table in the job summary shows
`raise baseline to <figure>`. Copy that figure into `coverage-baseline.json` in the pull request
that added the tests. Lowering a figure needs a reason in the pull request description.

## Local usage

```sh
export GO_COVERAGE_DIR="$(mktemp -d)"
scripts/check-go-cli-source.sh
cd cli/release-automation
go run ./cmd/coverage-report --baseline ../../coverage-baseline.json --mode report \
  --go-coverage-dir "$GO_COVERAGE_DIR"
```

Use a fresh directory each time: the report reads every `<module>.out` in it, so a profile left
from an earlier run would be compared too. `GO_COVERAGE_DIR` must be absolute, because each module's tests run from that module's
directory. Figures measured on macOS or Windows can differ slightly from the Linux gate.
