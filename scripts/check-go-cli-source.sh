#!/bin/sh
set -eu

ROOT_DIR=$(CDPATH= cd "$(dirname "$0")/.." && pwd)
CLI_DIR="$ROOT_DIR/cli"

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
    golangci-lint fmt --config "$CLI_DIR/.golangci.yml" --diff
    go vet $packages
    golangci-lint run --config "$CLI_DIR/.golangci.yml" $packages
    go test $packages
  )
}

run_module_checks "$ROOT_DIR/common"
run_module_checks "$ROOT_DIR/dispatcher"
run_module_checks "$ROOT_DIR/tools/release-automation"
run_module_checks "$CLI_DIR"
