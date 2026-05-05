#!/bin/sh
set -eu

REPOSITORY="hatayama/unity-cli-loop"
INSTALL_DIR="${ULOOP_INSTALL_DIR:-$HOME/.local/bin}"
VERSION="${ULOOP_VERSION:-latest}"

report_path_shadowing() {
  resolved_uloop=$(command -v uloop 2>/dev/null || true)
  expected_uloop="$INSTALL_DIR/$installed_command_name"

  if [ -z "$resolved_uloop" ] || [ "$resolved_uloop" = "$expected_uloop" ] || [ "$resolved_uloop.exe" = "$expected_uloop" ]; then
    return
  fi

  echo "Installed uloop to $expected_uloop, but PATH resolves uloop to:"
  echo "  $resolved_uloop"
  echo "Move $INSTALL_DIR earlier in PATH, or remove the legacy installation if it owns that command."
}

detect_asset_name() {
  os=$(uname -s)
  arch=$(uname -m)

  case "$os" in
    Darwin) os_name="darwin" ;;
    MINGW*|MSYS*) os_name="windows" ;;
    *)
      echo "Unsupported OS: $os" >&2
      exit 1
      ;;
  esac

  case "$arch" in
    arm64|aarch64) arch_name="arm64" ;;
    x86_64|amd64) arch_name="amd64" ;;
    *)
      echo "Unsupported architecture: $arch" >&2
      exit 1
    ;;
  esac

  if [ "$os_name" = "windows" ]; then
    if [ "$arch_name" != "amd64" ]; then
      echo "Unsupported Windows architecture: $arch" >&2
      exit 1
    fi
    echo "uloop-windows-amd64.zip"
    return
  fi

  echo "uloop-$os_name-$arch_name.tar.gz"
}

detect_installed_command_name() {
  case "$asset_name" in
    *.zip) echo "uloop.exe" ;;
    *) echo "uloop" ;;
  esac
}

find_latest_asset_url() {
  page=1

  while :; do
    releases_json=$(curl -fsSL "https://api.github.com/repos/$REPOSITORY/releases?per_page=100&page=$page")
    asset_url=$(printf '%s\n' "$releases_json" | awk -v asset_name="$asset_name" '
      /"browser_download_url":/ {
        line = $0
        sub(/^[[:space:]]*"browser_download_url": "/, "", line)
        sub(/",?[[:space:]]*$/, "", line)
        count = split(line, parts, "/")
        if (parts[count] == asset_name && found == "") {
          found = line
        }
      }
      END {
        if (found != "") {
          print found
        }
      }
    ')

    if [ -n "$asset_url" ]; then
      echo "$asset_url"
      return
    fi

    release_count=$(printf '%s\n' "$releases_json" | awk '/"tag_name":/ { count++ } END { print count + 0 }')
    if [ "$release_count" -lt 100 ]; then
      return
    fi

    page=$((page + 1))
  done
}

set_download_urls() {
  if [ "$VERSION" != "latest" ]; then
    download_url="https://github.com/$REPOSITORY/releases/download/$VERSION/$asset_name"
    checksum_url="$download_url.sha256"
    return
  fi

  download_url=$(find_latest_asset_url)
  if [ -z "$download_url" ]; then
    echo "Could not find a latest release asset named $asset_name." >&2
    echo "Set ULOOP_VERSION to a release tag that provides this asset." >&2
    exit 1
  fi
  checksum_url="$download_url.sha256"
}

extract_asset() {
  case "$asset_name" in
    *.zip)
      if ! command -v unzip >/dev/null 2>&1; then
        echo "unzip is required to extract $asset_name" >&2
        exit 1
      fi
      unzip -q "$tmp_dir/$asset_name" -d "$tmp_dir"
      if [ ! -f "$tmp_dir/$installed_command_name" ]; then
        echo "Expected $installed_command_name at archive root after extracting $asset_name." >&2
        exit 1
      fi
      return
      ;;
    *)
      tar -xzf "$tmp_dir/$asset_name" -C "$tmp_dir"
      ;;
  esac
}

asset_name=$(detect_asset_name)
installed_command_name=$(detect_installed_command_name)
download_url=""
checksum_url=""
set_download_urls

tmp_dir=$(mktemp -d)
staged_uloop_path=""
trap 'rm -rf "$tmp_dir"; if [ -n "$staged_uloop_path" ]; then rm -f "$staged_uloop_path"; fi' EXIT

verify_checksum() {
  if command -v sha256sum >/dev/null 2>&1; then
    (
      cd "$tmp_dir"
      sha256sum -c "$asset_name.sha256"
    )
    return
  fi

  if command -v shasum >/dev/null 2>&1; then
    expected_hash=$(awk '{print $1}' "$tmp_dir/$asset_name.sha256")
    actual_hash=$(shasum -a 256 "$tmp_dir/$asset_name" | awk '{print $1}')
    if [ "$expected_hash" != "$actual_hash" ]; then
      echo "Checksum mismatch for $asset_name" >&2
      exit 1
    fi
    return
  fi

  echo "sha256sum or shasum is required to verify $asset_name" >&2
  exit 1
}

mkdir -p "$INSTALL_DIR"
curl -fsSL "$download_url" -o "$tmp_dir/$asset_name"
curl -fsSL "$checksum_url" -o "$tmp_dir/$asset_name.sha256"
verify_checksum
extract_asset
staged_uloop_path="$INSTALL_DIR/.uloop-install-$$"
if [ "$installed_command_name" = "uloop.exe" ]; then
  staged_uloop_path="$staged_uloop_path.exe"
fi
install -m 0755 "$tmp_dir/$installed_command_name" "$staged_uloop_path"
"$staged_uloop_path" --version >/dev/null
mv -f "$staged_uloop_path" "$INSTALL_DIR/$installed_command_name"
staged_uloop_path=""

case ":$PATH:" in
  *":$INSTALL_DIR:"*) ;;
  *)
    echo "Installed uloop to $INSTALL_DIR, but that directory is not in PATH."
    echo "Add this to your shell profile:"
    echo "  export PATH=\"$INSTALL_DIR:\$PATH\""
    ;;
esac

"$INSTALL_DIR/$installed_command_name" --version
report_path_shadowing
