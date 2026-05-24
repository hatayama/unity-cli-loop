#!/bin/sh
set -eu

ROOT_DIR=$(CDPATH= cd "$(dirname "$0")/.." && pwd)
CLI_DIR="$ROOT_DIR/Packages/src/Cli~"

. "$ROOT_DIR/scripts/go-cli-toolchain.sh"
require_go_cli_toolchain "$ROOT_DIR"

(cd "$CLI_DIR" && go run ./cmd/cli-minimum-version-guard)
