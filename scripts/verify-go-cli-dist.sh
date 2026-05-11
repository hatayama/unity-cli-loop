#!/bin/sh
set -eu

ROOT_DIR=$(CDPATH= cd "$(dirname "$0")/.." && pwd)

DIST_FILES="
Packages/src/Cli~/dist/darwin-arm64/uloop
Packages/src/Cli~/dist/darwin-amd64/uloop
Packages/src/Cli~/dist/windows-amd64/uloop.exe
"

"$ROOT_DIR/scripts/build-go-cli.sh"

for dist_file in $DIST_FILES; do
  if [ ! -f "$ROOT_DIR/$dist_file" ]; then
    echo "Checked-in native CLI binary is missing: $dist_file" >&2
    echo "Run scripts/build-go-cli.sh and commit the updated CLI dist files." >&2
    exit 1
  fi
done

UNTRACKED_DIST_FILES=$(git -C "$ROOT_DIR" ls-files --others --exclude-standard -- $DIST_FILES)
if [ -n "$UNTRACKED_DIST_FILES" ]; then
  echo "Native CLI dist files are untracked:" >&2
  printf '%s\n' "$UNTRACKED_DIST_FILES" >&2
  echo "Commit the updated CLI dist files." >&2
  exit 1
fi

if git -C "$ROOT_DIR" diff --exit-code -- $DIST_FILES; then
  exit 0
fi

echo "Checked-in native CLI binaries are out of date." >&2
echo "Run scripts/build-go-cli.sh and commit the updated CLI dist files." >&2
exit 1
