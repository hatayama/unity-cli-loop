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

run_module_checks() {
  module_dir="$1"

  (
    cd "$module_dir"
    module_root=$(pwd)
    packages=$(go list -f '{{.Dir}}' ./... | grep -v '/node_modules/' | awk -v root="$module_root" '
      $0 == root {
        print "."
        next
      }
      index($0, root "/") == 1 {
        print "./" substr($0, length(root) + 2)
      }
    ')
    golangci-lint fmt --config "$ROOT_DIR/cli/.golangci.yml" --diff
    go vet $packages
    golangci-lint run --config "$ROOT_DIR/cli/.golangci.yml" $packages
    go test $packages
  )
}

run_module_checks "$ROOT_DIR/cli/common"
run_module_checks "$ROOT_DIR/cli/dispatcher"
run_module_checks "$ROOT_DIR/cli/release-automation"
run_module_checks "$RUNNER_DIR"
