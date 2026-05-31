#!/bin/sh
set -eu

ROOT_DIR=$(CDPATH= cd "$(dirname "$0")/.." && pwd)

cd "$ROOT_DIR/cli"
go test ./internal/architecture -run TestCommentCliMinimumVersionWarning -count=1
