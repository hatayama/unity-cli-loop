#!/bin/sh
set -eu

ROOT_DIR=$(CDPATH= cd "$(dirname "$0")/.." && pwd)
GO_CLI_DIR="$ROOT_DIR/Packages/src/GoCli~"

. "$ROOT_DIR/scripts/go-cli-toolchain.sh"
require_go_cli_toolchain "$ROOT_DIR"

if ! command -v golangci-lint >/dev/null 2>&1; then
  echo "golangci-lint is required. Install it before running Go CLI checks." >&2
  echo "https://golangci-lint.run/welcome/install/" >&2
  exit 1
fi

(
  cd "$GO_CLI_DIR"
  golangci-lint fmt --diff
  go vet ./...
  golangci-lint run ./...
  go test ./...
)

"$ROOT_DIR/scripts/verify-go-cli-dist.sh"
