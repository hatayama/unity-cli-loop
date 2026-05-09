#!/bin/sh
set -eu

ROOT_DIR=$(CDPATH= cd "$(dirname "$0")/.." && pwd)

DIST_FILES="
Packages/src/Cli~/Core~/dist/darwin-arm64/uloop-core
Packages/src/Cli~/Core~/dist/darwin-amd64/uloop-core
Packages/src/Cli~/Core~/dist/windows-amd64/uloop-core.exe
"

"$ROOT_DIR/scripts/build-go-cli.sh"

if git -C "$ROOT_DIR" diff --exit-code -- $DIST_FILES; then
  exit 0
fi

echo "Checked-in Core CLI binaries are out of date." >&2
echo "Run scripts/build-go-cli.sh and commit the updated Core dist files." >&2
exit 1
