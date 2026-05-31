#!/bin/sh
set -eu

ROOT_DIR=$(CDPATH= cd "$(dirname "$0")/.." && pwd)
WORK_DIR="${TMPDIR:-/tmp}/uloop-use-local-test-$$"
BIN_DIR="$WORK_DIR/bin"

case "$(uname -s)" in
  Darwin)
    ;;
  *)
    echo "use-local-uloop test skipped on unsupported platform"
    exit 0
    ;;
esac

cleanup() {
  rm -rf "$WORK_DIR"
}

trap cleanup EXIT INT HUP TERM

mkdir -p "$BIN_DIR"
printf '%s\n' '#!/bin/sh' 'echo old-global-uloop' > "$BIN_DIR/uloop"
chmod +x "$BIN_DIR/uloop"

ULOOP_GLOBAL_BIN_DIR="$BIN_DIR" "$ROOT_DIR/scripts/use-local-uloop.sh" link >/dev/null

if [ ! -L "$BIN_DIR/uloop" ]; then
  echo "Expected global uloop to be a symlink after link." >&2
  exit 1
fi

expected_target="$ROOT_DIR/Packages/src/Cli~/dist/darwin-arm64/uloop"
if [ "$(uname -m)" = "x86_64" ] || [ "$(uname -m)" = "amd64" ]; then
  expected_target="$ROOT_DIR/Packages/src/Cli~/dist/darwin-amd64/uloop"
fi

actual_target=$(readlink "$BIN_DIR/uloop")
if [ "$actual_target" != "$expected_target" ]; then
  echo "Unexpected symlink target: $actual_target" >&2
  echo "Expected: $expected_target" >&2
  exit 1
fi

if [ ! -x "$BIN_DIR/uloop.before-local-link" ]; then
  echo "Expected original global uloop backup to exist." >&2
  exit 1
fi

ULOOP_GLOBAL_BIN_DIR="$BIN_DIR" "$ROOT_DIR/scripts/use-local-uloop.sh" restore >/dev/null

if [ -L "$BIN_DIR/uloop" ]; then
  echo "Expected restored global uloop to no longer be a symlink." >&2
  exit 1
fi

if [ "$("$BIN_DIR/uloop")" != "old-global-uloop" ]; then
  echo "Restored global uloop did not execute the original script." >&2
  exit 1
fi

if [ -e "$BIN_DIR/uloop.before-local-link" ]; then
  echo "Expected backup to be consumed after restore." >&2
  exit 1
fi

TRAILING_HOME="$WORK_DIR/trailing-home"
TRAILING_BIN="$TRAILING_HOME/.local/bin"
mkdir -p "$TRAILING_BIN"

HOME="$TRAILING_HOME" PATH="$TRAILING_BIN/:/usr/bin:/bin:/usr/sbin:/sbin" "$ROOT_DIR/scripts/use-local-uloop.sh" link >/dev/null

if [ ! -L "$TRAILING_BIN/uloop" ]; then
  echo "Expected PATH entry with trailing slash to be selected for global uloop." >&2
  exit 1
fi

echo "use-local-uloop link/restore test passed"
