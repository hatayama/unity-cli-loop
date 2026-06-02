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

if [ -L "$BIN_DIR/uloop" ]; then
  echo "Expected global uloop to be a copied executable, not a symlink." >&2
  exit 1
fi

expected_target="$ROOT_DIR/cli/dist/darwin-arm64/uloop"
if [ "$(uname -m)" = "x86_64" ] || [ "$(uname -m)" = "amd64" ]; then
  expected_target="$ROOT_DIR/cli/dist/darwin-amd64/uloop"
fi

if ! cmp -s "$BIN_DIR/uloop" "$expected_target"; then
  echo "Expected global uloop to match the local development binary." >&2
  exit 1
fi

if [ -e "$BIN_DIR/uloop.before-local-link" ]; then
  echo "Expected link to avoid creating a backup." >&2
  exit 1
fi

printf '%s\n' '#!/bin/sh' 'echo stale-backup' > "$BIN_DIR/uloop.before-local-link"
chmod +x "$BIN_DIR/uloop.before-local-link"

ULOOP_GLOBAL_BIN_DIR="$BIN_DIR" "$ROOT_DIR/scripts/use-local-uloop.sh" link >/dev/null

if ! cmp -s "$BIN_DIR/uloop" "$expected_target"; then
  echo "Expected link to overwrite the global uloop even when a stale backup exists." >&2
  exit 1
fi

if [ "$("$BIN_DIR/uloop.before-local-link")" != "stale-backup" ]; then
  echo "Expected stale backup to be left untouched." >&2
  exit 1
fi

TRAILING_HOME="$WORK_DIR/trailing-home"
TRAILING_BIN="$TRAILING_HOME/.local/bin"
mkdir -p "$TRAILING_BIN"

HOME="$TRAILING_HOME" PATH="$TRAILING_BIN/:/usr/bin:/bin:/usr/sbin:/sbin" "$ROOT_DIR/scripts/use-local-uloop.sh" link >/dev/null

if [ -L "$TRAILING_BIN/uloop" ] || [ ! -x "$TRAILING_BIN/uloop" ]; then
  echo "Expected PATH entry with trailing slash to be selected for global uloop." >&2
  exit 1
fi

if ! cmp -s "$TRAILING_BIN/uloop" "$expected_target"; then
  echo "Expected trailing PATH global uloop to match the local development binary." >&2
  exit 1
fi

echo "use-local-uloop copy-link test passed"
