#!/bin/sh
set -eu

ROOT_DIR=$(CDPATH= cd "$(dirname "$0")/.." && pwd)
CLI_DIR="$ROOT_DIR/Packages/src/Cli~"

. "$ROOT_DIR/scripts/go-cli-toolchain.sh"
require_go_cli_toolchain "$ROOT_DIR"

if ! command -v golangci-lint >/dev/null 2>&1; then
  echo "golangci-lint is required. Install it before running Go CLI checks." >&2
  echo "https://golangci-lint.run/welcome/install/" >&2
  exit 1
fi

run_module_checks() {
  module_dir="$1"

  (
    cd "$module_dir"
    golangci-lint fmt --config "$CLI_DIR/.golangci.yml" --diff
    go vet ./...
    golangci-lint run --config "$CLI_DIR/.golangci.yml" ./...
    go test ./...
  )
}

run_module_checks "$CLI_DIR/Shared~"
run_module_checks "$CLI_DIR/Core~"
run_module_checks "$CLI_DIR/Dispatcher~"
