#!/bin/sh
set -eu

ROOT_DIR=$(CDPATH= cd "$(dirname "$0")/.." && pwd)
RUNNER_DIR="$ROOT_DIR/cli/project-runner"

. "$ROOT_DIR/scripts/go-cli-toolchain.sh"
require_go_cli_toolchain "$ROOT_DIR"

if ! command -v golangci-lint >/dev/null 2>&1; then
  echo "golangci-lint is required. Install it before running Go CLI checks." >&2
  echo "https://golangci-lint.run/welcome/install/" >&2
  exit 1
fi

# GO_COVERAGE_DIR, when set, collects one coverage profile per module as
# <module>.out for cli/release-automation/cmd/coverage-report. It must be
# absolute because each module's tests run from that module's directory.
if [ -n "${GO_COVERAGE_DIR:-}" ]; then
  case "$GO_COVERAGE_DIR" in
    /*) mkdir -p "$GO_COVERAGE_DIR" ;;
    *)
      echo "GO_COVERAGE_DIR must be an absolute path: $GO_COVERAGE_DIR" >&2
      exit 1
      ;;
  esac
fi

run_module_checks() {
  module_dir="$1"

  (
    cd "$module_dir"
    golangci-lint fmt --config "$ROOT_DIR/cli/.golangci.yml" --diff
    go vet ./...
    golangci-lint run --config "$ROOT_DIR/cli/.golangci.yml" ./...
    if [ -n "${GO_COVERAGE_DIR:-}" ]; then
      go test -coverprofile="$GO_COVERAGE_DIR/$(basename "$module_dir").out" ./...
    else
      go test ./...
    fi
  )
}

run_module_checks "$ROOT_DIR/cli/common"
run_module_checks "$ROOT_DIR/cli/dispatcher"
run_module_checks "$ROOT_DIR/cli/release-automation"
run_module_checks "$RUNNER_DIR"
