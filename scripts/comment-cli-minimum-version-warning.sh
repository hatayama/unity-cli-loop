#!/bin/sh
set -eu

ROOT_DIR=${ULOOP_REPOSITORY_ROOT:-$(CDPATH= cd "$(dirname "$0")/.." && pwd)}
CLI_DIR=$(CDPATH= cd "$(dirname "$0")/../Packages/src/Cli~" && pwd)

cd "$CLI_DIR"
ULOOP_REPOSITORY_ROOT="$ROOT_DIR" go run ./cmd/comment-cli-minimum-version-warning
