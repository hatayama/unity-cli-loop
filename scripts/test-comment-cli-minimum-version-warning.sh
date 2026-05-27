#!/bin/sh
set -eu

ROOT_DIR=$(CDPATH= cd "$(dirname "$0")/.." && pwd)

cd "$ROOT_DIR/Packages/src/Cli~"
go test ./internal/architecture -run TestCommentCliMinimumVersionWarning
