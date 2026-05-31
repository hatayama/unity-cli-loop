#!/bin/sh
set -eu

ROOT_DIR=$(CDPATH= cd "$(dirname "$0")/.." && pwd)
ACTION="${1:-status}"

command_name="uloop"

normalize_path_dir() {
  path_dir="$1"
  while [ "$path_dir" != "/" ] && [ "${path_dir%/}" != "$path_dir" ]; do
    path_dir=${path_dir%/}
  done
  printf '%s\n' "$path_dir"
}

select_local_uloop() {
  os_name=$(uname -s)
  arch_name=$(uname -m)

  case "$os_name:$arch_name" in
    Darwin:arm64|Darwin:aarch64)
      local_uloop="$ROOT_DIR/cli/dist/darwin-arm64/uloop"
      ;;
    Darwin:x86_64|Darwin:amd64)
      local_uloop="$ROOT_DIR/cli/dist/darwin-amd64/uloop"
      ;;
    *)
      echo "No built local uloop binary for $os_name $arch_name." >&2
      exit 1
      ;;
  esac

  if [ ! -x "$local_uloop" ]; then
    echo "Local uloop binary is missing or not executable: $local_uloop" >&2
    exit 1
  fi
}

path_contains_dir() {
  candidate_dir=$(normalize_path_dir "$1")
  old_ifs=$IFS
  IFS=:
  for path_dir in $PATH; do
    normalized_path_dir=$(normalize_path_dir "$path_dir")
    if [ "$normalized_path_dir" = "$candidate_dir" ]; then
      IFS=$old_ifs
      return 0
    fi
  done
  IFS=$old_ifs
  return 1
}

select_global_bin_dir() {
  if [ "${ULOOP_GLOBAL_BIN_DIR:-}" ]; then
    global_bin_dir=$ULOOP_GLOBAL_BIN_DIR
    return
  fi

  existing_uloop=$(command -v "$command_name" 2>/dev/null || true)
  if [ "$existing_uloop" ]; then
    global_bin_dir=$(dirname "$existing_uloop")
    return
  fi

  npm_global_bin="$HOME/.npm-global/bin"
  if path_contains_dir "$npm_global_bin" || [ -e "$npm_global_bin/$command_name" ]; then
    global_bin_dir=$npm_global_bin
    return
  fi

  local_bin="$HOME/.local/bin"
  if path_contains_dir "$local_bin"; then
    global_bin_dir=$local_bin
    return
  fi

  echo "No PATH directory found for global uloop. Set ULOOP_GLOBAL_BIN_DIR." >&2
  exit 1
}

print_status() {
  echo "Global uloop path: $global_uloop"
  if [ -L "$global_uloop" ]; then
    echo "Global uloop symlink target: $(readlink "$global_uloop")"
  elif [ -e "$global_uloop" ]; then
    echo "Global uloop is a regular file or directory."
  else
    echo "Global uloop does not exist."
  fi

  if [ -e "$backup_uloop" ] || [ -L "$backup_uloop" ]; then
    echo "Backup path: $backup_uloop"
  else
    echo "Backup path: none"
  fi

  echo "Local branch uloop: $local_uloop"
  if [ -x "$global_uloop" ]; then
    echo "Selected global uloop version:"
    "$global_uloop" --version || true
  fi

  if command -v "$command_name" >/dev/null 2>&1; then
    echo "Resolved uloop: $(command -v "$command_name")"
  fi
}

link_local_uloop() {
  mkdir -p "$global_bin_dir"

  if [ -L "$global_uloop" ] && [ "$(readlink "$global_uloop")" = "$local_uloop" ]; then
    echo "Global uloop already points to this checkout."
    print_status
    return
  fi

  if [ -e "$global_uloop" ] || [ -L "$global_uloop" ]; then
    if [ -e "$backup_uloop" ] || [ -L "$backup_uloop" ]; then
      echo "Backup already exists, refusing to overwrite it: $backup_uloop" >&2
      echo "Run '$0 restore' first or move the backup manually." >&2
      exit 1
    fi

    mv "$global_uloop" "$backup_uloop"
    echo "Backed up existing global uloop to $backup_uloop"
  fi

  ln -s "$local_uloop" "$global_uloop"
  print_status
}

restore_global_uloop() {
  if [ ! -e "$backup_uloop" ] && [ ! -L "$backup_uloop" ]; then
    echo "No backup found: $backup_uloop" >&2
    exit 1
  fi

  if [ -L "$global_uloop" ]; then
    current_target=$(readlink "$global_uloop")
    if [ "$current_target" != "$local_uloop" ]; then
      echo "Refusing to remove unexpected uloop symlink: $global_uloop -> $current_target" >&2
      exit 1
    fi

    rm "$global_uloop"
  elif [ -e "$global_uloop" ]; then
    echo "Refusing to overwrite unexpected global uloop: $global_uloop" >&2
    exit 1
  fi

  mv "$backup_uloop" "$global_uloop"
  print_status
}

usage() {
  echo "Usage: $0 link|restore|status" >&2
  exit 1
}

select_local_uloop
select_global_bin_dir

global_uloop="$global_bin_dir/$command_name"
backup_uloop="$global_bin_dir/$command_name.before-local-link"

case "$ACTION" in
  link)
    link_local_uloop
    ;;
  restore)
    restore_global_uloop
    ;;
  status)
    print_status
    ;;
  *)
    usage
    ;;
esac
