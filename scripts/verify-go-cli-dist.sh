#!/bin/sh
set -eu

ROOT_DIR=$(CDPATH= cd "$(dirname "$0")/.." && pwd)

DIST_FILES="
cli/dist/darwin-arm64/uloop
cli/dist/darwin-amd64/uloop
cli/dist/windows-amd64/uloop.exe
"

"$ROOT_DIR/scripts/build-go-cli.sh"

for dist_file in $DIST_FILES; do
  if [ ! -f "$ROOT_DIR/$dist_file" ]; then
    echo "Built native CLI binary is missing: $dist_file" >&2
    echo "Run scripts/build-go-cli.sh and rerun this check." >&2
    exit 1
  fi
done
