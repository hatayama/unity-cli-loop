#!/bin/sh
set -eu

REPOSITORY="hatayama/unity-cli-loop"
INSTALL_DIR="${ULOOP_INSTALL_DIR:-$HOME/.local/bin}"
VERSION="${ULOOP_VERSION:-latest}"
LEGACY_NPM_PACKAGE="uloop-cli"
REMOVE_LEGACY="${ULOOP_REMOVE_LEGACY:-0}"
legacy_cleanup_failed=0

is_remove_legacy_enabled() {
  case "$REMOVE_LEGACY" in
    1|true|TRUE|yes|YES) return 0 ;;
    *) return 1 ;;
  esac
}

is_legacy_npm_installed() {
  command -v npm >/dev/null 2>&1 && npm list -g "$LEGACY_NPM_PACKAGE" --depth=0 >/dev/null 2>&1
}

remove_legacy_npm_if_enabled() {
  if ! is_legacy_npm_installed; then
    return
  fi

  if is_remove_legacy_enabled; then
    echo "Removing legacy npm installation: $LEGACY_NPM_PACKAGE"
    if ! npm uninstall -g "$LEGACY_NPM_PACKAGE"; then
      legacy_cleanup_failed=1
      echo "Warning: Could not remove legacy npm installation: $LEGACY_NPM_PACKAGE"
      echo "To remove it manually, run:"
      echo "  npm uninstall -g $LEGACY_NPM_PACKAGE"
    fi
  fi
}

ensure_active_uloop_after_legacy_cleanup() {
  if [ "$legacy_cleanup_failed" -ne 1 ]; then
    return
  fi

  resolved_uloop=$(command -v uloop 2>/dev/null || true)
  expected_uloop="$INSTALL_DIR/uloop"
  if [ -z "$resolved_uloop" ] || [ "$resolved_uloop" = "$expected_uloop" ]; then
    return
  fi

  echo "Failed to remove legacy npm installation, and PATH still resolves uloop to:" >&2
  echo "  $resolved_uloop" >&2
  echo "The native dispatcher was installed to $expected_uloop, but running uloop may still use the legacy command." >&2
  echo "Remove the legacy package manually, or move $INSTALL_DIR earlier in PATH." >&2
  exit 1
}

report_legacy_npm_if_present() {
  if is_remove_legacy_enabled || ! is_legacy_npm_installed; then
    return
  fi

  echo "Legacy npm installation detected: $LEGACY_NPM_PACKAGE"
  echo "The native dispatcher was installed, but the npm package may still provide an older uloop command."
  echo "To remove it, run:"
  echo "  npm uninstall -g $LEGACY_NPM_PACKAGE"
  echo "Or rerun this installer with:"
  echo "  ULOOP_REMOVE_LEGACY=1"
}

report_path_shadowing() {
  resolved_uloop=$(command -v uloop 2>/dev/null || true)
  expected_uloop="$INSTALL_DIR/uloop"

  if [ -z "$resolved_uloop" ] || [ "$resolved_uloop" = "$expected_uloop" ]; then
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

  echo "uloop-$os_name-$arch_name.tar.gz"
}

asset_name=$(detect_asset_name)
if [ "$VERSION" = "latest" ]; then
  download_url="https://github.com/$REPOSITORY/releases/latest/download/$asset_name"
else
  download_url="https://github.com/$REPOSITORY/releases/download/$VERSION/$asset_name"
fi
checksum_url="$download_url.sha256"

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
tar -xzf "$tmp_dir/$asset_name" -C "$tmp_dir"
staged_uloop_path="$INSTALL_DIR/.uloop-install-$$"
install -m 0755 "$tmp_dir/uloop" "$staged_uloop_path"
"$staged_uloop_path" --version >/dev/null
remove_legacy_npm_if_enabled
mv -f "$staged_uloop_path" "$INSTALL_DIR/uloop"
staged_uloop_path=""

case ":$PATH:" in
  *":$INSTALL_DIR:"*) ;;
  *)
    echo "Installed uloop to $INSTALL_DIR, but that directory is not in PATH."
    echo "Add this to your shell profile:"
    echo "  export PATH=\"$INSTALL_DIR:\$PATH\""
    ;;
esac

"$INSTALL_DIR/uloop" --version
ensure_active_uloop_after_legacy_cleanup
report_legacy_npm_if_present
report_path_shadowing
