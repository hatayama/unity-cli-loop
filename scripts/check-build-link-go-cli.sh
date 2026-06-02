#!/bin/sh
set -eu

ROOT_DIR=$(CDPATH= cd "$(dirname "$0")/.." && pwd)

path_contains_dir() {
  expected_dir=$(normalize_path_dir "$1")
  old_ifs=$IFS
  IFS=:
  for path_dir in $PATH; do
    if [ "$(normalize_path_dir "$path_dir")" = "$expected_dir" ]; then
      IFS=$old_ifs
      return 0
    fi
  done
  IFS=$old_ifs
  return 1
}

normalize_path_dir() {
  path_dir="$1"
  while [ "$path_dir" != "/" ] && [ "${path_dir%/}" != "$path_dir" ]; do
    path_dir=${path_dir%/}
  done
  printf '%s\n' "$path_dir"
}

copy_uloop_binary() {
  destination_path="$1"
  tmp_destination_path="$destination_path.tmp.$$"

  rm -f "$tmp_destination_path"
  cp "$cli_path" "$tmp_destination_path"
  chmod +x "$tmp_destination_path"
  mv -f "$tmp_destination_path" "$destination_path"
  echo "Copied rebuilt native CLI to global uloop: $destination_path"
}

ensure_global_uloop_resolves_to_updated_binary() {
  resolved_uloop_path=$(command -v "$global_command_name" || true)

  if [ "$resolved_uloop_path" = "$global_uloop_path" ]; then
    return 0
  fi
  if [ -n "$extra_global_uloop_path" ] && [ "$resolved_uloop_path" = "$extra_global_uloop_path" ]; then
    return 0
  fi

  echo "Global $global_command_name was updated, but shell resolution does not point at it." >&2
  echo "Resolved $global_command_name: ${resolved_uloop_path:-not found}" >&2
  echo "Expected $global_command_name: $global_uloop_path" >&2
  echo "Add $global_bin_dir to PATH or set ULOOP_GLOBAL_BIN_DIR to a directory earlier in PATH." >&2
  exit 1
}

"$ROOT_DIR/scripts/check-go-cli-source.sh"
"$ROOT_DIR/scripts/build-go-cli.sh"

cli_path=""
global_command_name="uloop"
existing_uloop_path=""
os=$(uname -s)
arch=$(uname -m)

case "$os:$arch" in
  Darwin:arm64 | Darwin:aarch64)
    cli_path="$ROOT_DIR/cli/dist/darwin-arm64/uloop"
    ;;
  Darwin:x86_64 | Darwin:amd64)
    cli_path="$ROOT_DIR/cli/dist/darwin-amd64/uloop"
    ;;
  MINGW*:x86_64 | MINGW*:amd64 | MSYS*:x86_64 | MSYS*:amd64 | CYGWIN*:x86_64 | CYGWIN*:amd64 | Windows_NT:x86_64 | Windows_NT:amd64)
    cli_path="$ROOT_DIR/cli/dist/windows-amd64/uloop.exe"
    global_command_name="uloop.exe"
    ;;
esac

if [ -z "$cli_path" ]; then
  echo "Go CLI source checks passed and dist binaries were rebuilt."
  echo "No built native CLI is mapped for this platform: $os/$arch"
  exit 0
fi

if [ ! -x "$cli_path" ]; then
  echo "Native CLI was not built or is not executable: $cli_path" >&2
  exit 1
fi

global_bin_dir=""

if [ -n "${ULOOP_GLOBAL_BIN_DIR:-}" ]; then
  global_bin_dir="$ULOOP_GLOBAL_BIN_DIR"
elif command -v "$global_command_name" >/dev/null 2>&1; then
  existing_uloop_path=$(command -v "$global_command_name")
  global_bin_dir=$(dirname "$existing_uloop_path")
elif [ "$global_command_name" = "uloop.exe" ] && command -v uloop >/dev/null 2>&1; then
  existing_uloop_path=$(command -v uloop)
  global_bin_dir=$(dirname "$existing_uloop_path")
elif path_contains_dir "$HOME/.npm-global/bin"; then
  global_bin_dir="$HOME/.npm-global/bin"
elif path_contains_dir "$HOME/.local/bin"; then
  global_bin_dir="$HOME/.local/bin"
fi

if [ -z "$global_bin_dir" ]; then
  echo "Go CLI source checks passed and dist binaries were rebuilt."
  echo "No writable PATH directory was selected for global uloop." >&2
  echo "Set ULOOP_GLOBAL_BIN_DIR to the directory that should contain uloop." >&2
  exit 1
fi

mkdir -p "$global_bin_dir"
global_bin_dir=$(CDPATH= cd "$global_bin_dir" && pwd)
global_uloop_path="$global_bin_dir/$global_command_name"
extra_global_uloop_path=""

if [ "$global_command_name" = "uloop.exe" ] && [ -n "$existing_uloop_path" ] && [ "$existing_uloop_path" != "$global_uloop_path" ]; then
  existing_uloop_dir=$(dirname "$existing_uloop_path")
  if [ "$existing_uloop_dir" = "$global_bin_dir" ]; then
    extra_global_uloop_path="$existing_uloop_path"
  fi
fi

echo "Go CLI source checks passed and dist binaries were rebuilt."

copy_uloop_binary "$global_uloop_path"
if [ -n "$extra_global_uloop_path" ]; then
  copy_uloop_binary "$extra_global_uloop_path"
fi

ensure_global_uloop_resolves_to_updated_binary

"$global_command_name" --version
