# Test Coverage

The repository tracks statement coverage of the Go CLI modules and keeps it from falling, and
records C# line coverage of the Unity package every night.

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

## C# coverage

C# line coverage is measured only in the nightly `unity-editmode-tests.yml` run, in the Unity
2022.3 leg, which runs the full EditMode suite. The other legs run the hot-reload suites alone,
and PlayMode tests are not measured. The Unity Code Coverage package keeps the shipped
`UnityCLILoop.*` assemblies and drops tests, dev tooling, and samples.

### The measured scope

The figure compared with `csharp.lineCoverage` in `coverage-baseline.json` is the covered lines
divided by the coverable lines of every assembly except those listed under `csharp.exclude`,
truncated to one decimal. The excluded assemblies run only in Play Mode: the runtime assembly,
input recording and replay, keyboard and mouse simulation, and the Game View overlay. Their tests
live in the PlayMode suite, so the EditMode leg cannot measure them. They still appear in the
report, in a separate table, so their trend stays visible.

`UnityCLILoop.Presentation` (the Editor UI) and the video recording tool stay in the scope,
because both have EditMode tests. View classes count toward the figure even though views are not
unit tested; the figure is for following the trend, not a target for views.

An exclude entry that matches no assembly in the report fails the run, so a misspelled name
cannot leave the assembly it meant to drop inside the scope.

### The trend issue and the baseline

The `coverage-trend` job posts a comment every night on the open issue labelled
`coverage-trend`: the Go table, then the C# measured scope against its baseline, the scoped
assemblies sorted by uncovered lines, and the excluded ones. The job creates the label and the
issue when none is open, and fails when several are open; close all but one. When the 2022.3 leg
produced no coverage (no Unity license, or the leg failed before its tests ran), the comment
carries Go alone and says so.

C# coverage never gates. When the scope falls more than 0.1 point below `csharp.lineCoverage`,
the nightly run prints a warning and the comment marks it `below baseline`; pull requests do not
measure C# at all, and a broken `csharp` section does not affect the pull request Go gate.

### The HTML report

Each nightly run uploads the ReportGenerator output as the `csharp-coverage` artifact, kept for
14 days. Open `index.htm` at the artifact root for per-class and per-line detail.

```sh
gh run download <run id> -n csharp-coverage -D <directory>
```

To render the report Markdown locally from a downloaded `Summary.json`:

```sh
cd cli/release-automation
go run ./cmd/coverage-report --baseline ../../coverage-baseline.json --mode report \
  --go-coverage-dir "$GO_COVERAGE_DIR" --csharp-summary <directory>/Summary.json \
  --markdown-out <file>
```
