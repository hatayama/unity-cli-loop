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

ensure_symlink_target() {
  link_path="$1"

  if [ -e "$link_path" ] && [ ! -L "$link_path" ]; then
    echo "Go CLI source checks passed and dist binaries were rebuilt."
    echo "Refusing to overwrite non-symlink global uloop: $link_path" >&2
    exit 1
  fi
}

update_uloop_link() {
  link_path="$1"

  if [ -L "$link_path" ]; then
    current_target=$(readlink "$link_path")
    echo "Updating global uloop symlink: $link_path -> $current_target"
  else
    echo "Creating global uloop symlink: $link_path"
  fi

  ln -sfn "$dispatcher_path" "$link_path"
  echo "Global uloop now points at the rebuilt dispatcher: $link_path -> $(readlink "$link_path")"
}

install_project_local_core() {
  mkdir -p "$ROOT_DIR/.uloop/bin"
  rm -f "$project_local_core_path"
  cp "$core_path" "$project_local_core_path"
  chmod +x "$project_local_core_path"
  echo "Project-local uloop-core now uses the rebuilt binary: $project_local_core_path"
}

ensure_global_uloop_resolves_to_link() {
  resolved_uloop_path=$(command -v "$global_command_name" || true)

  if [ "$resolved_uloop_path" = "$global_uloop_path" ]; then
    return 0
  fi
  if [ -n "$extra_global_uloop_path" ] && [ "$resolved_uloop_path" = "$extra_global_uloop_path" ]; then
    return 0
  fi

  echo "Global $global_command_name symlink was updated, but shell resolution does not point at it." >&2
  echo "Resolved $global_command_name: ${resolved_uloop_path:-not found}" >&2
  echo "Expected $global_command_name: $global_uloop_path" >&2
  echo "Add $global_bin_dir to PATH or set ULOOP_GLOBAL_BIN_DIR to a directory earlier in PATH." >&2
  exit 1
}

"$ROOT_DIR/scripts/check-go-cli-source.sh"
"$ROOT_DIR/scripts/build-go-cli.sh"

core_path=""
dispatcher_path=""
global_command_name="uloop"
existing_uloop_path=""
project_local_core_path="$ROOT_DIR/.uloop/bin/uloop-core"
os=$(uname -s)
arch=$(uname -m)

case "$os:$arch" in
  Darwin:arm64 | Darwin:aarch64)
    core_path="$ROOT_DIR/Packages/src/Cli~/Core~/dist/darwin-arm64/uloop-core"
    dispatcher_path="$ROOT_DIR/Packages/src/Cli~/Dispatcher~/dist/darwin-arm64/uloop-dispatcher"
    ;;
  Darwin:x86_64 | Darwin:amd64)
    core_path="$ROOT_DIR/Packages/src/Cli~/Core~/dist/darwin-amd64/uloop-core"
    dispatcher_path="$ROOT_DIR/Packages/src/Cli~/Dispatcher~/dist/darwin-amd64/uloop-dispatcher"
    ;;
  MINGW*:x86_64 | MINGW*:amd64 | MSYS*:x86_64 | MSYS*:amd64 | CYGWIN*:x86_64 | CYGWIN*:amd64 | Windows_NT:x86_64 | Windows_NT:amd64)
    core_path="$ROOT_DIR/Packages/src/Cli~/Core~/dist/windows-amd64/uloop-core.exe"
    dispatcher_path="$ROOT_DIR/Packages/src/Cli~/Dispatcher~/dist/windows-amd64/uloop-dispatcher.exe"
    global_command_name="uloop.exe"
    project_local_core_path="$ROOT_DIR/.uloop/bin/uloop-core.exe"
    ;;
esac

if [ -z "$dispatcher_path" ] || [ -z "$core_path" ]; then
  echo "Go CLI source checks passed and dist binaries were rebuilt."
  echo "No checked-in dispatcher is mapped for this platform: $os/$arch"
  exit 0
fi

if [ ! -x "$core_path" ]; then
  echo "Project-local core was not built or is not executable: $core_path" >&2
  exit 1
fi

if [ ! -x "$dispatcher_path" ]; then
  echo "Dispatcher was not built or is not executable: $dispatcher_path" >&2
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

install_project_local_core

ensure_symlink_target "$global_uloop_path"
if [ -n "$extra_global_uloop_path" ]; then
  ensure_symlink_target "$extra_global_uloop_path"
fi

update_uloop_link "$global_uloop_path"
if [ -n "$extra_global_uloop_path" ]; then
  update_uloop_link "$extra_global_uloop_path"
fi

ensure_global_uloop_resolves_to_link

"$global_command_name" --version
