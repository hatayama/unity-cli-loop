#!/bin/sh
set -eu

ROOT_DIR=$(CDPATH= cd "$(dirname "$0")/.." && pwd)
EXPECTED_ASSETS="
install.sh
install.ps1
uloop-darwin-amd64.tar.gz
uloop-darwin-amd64.tar.gz.sha256
uloop-darwin-arm64.tar.gz
uloop-darwin-arm64.tar.gz.sha256
uloop-windows-amd64.zip
uloop-windows-amd64.zip.sha256
"

if [ "${1:-}" = "--list" ]; then
  printf '%s\n' $EXPECTED_ASSETS
  exit 0
fi

RELEASE_DIR="${1:-$ROOT_DIR/Packages/src/Cli~/Dispatcher~/release}"

fail() {
  echo "$1" >&2
  exit 1
}

require_file() {
  required_asset_name="$1"
  required_asset_path="$RELEASE_DIR/$required_asset_name"

  if [ ! -f "$required_asset_path" ]; then
    fail "Missing native CLI release asset: $required_asset_name"
  fi

  if [ ! -s "$required_asset_path" ]; then
    fail "Native CLI release asset is empty: $required_asset_name"
  fi
}

sha256_file() {
  checksum_asset_name="$1"
  if command -v sha256sum >/dev/null 2>&1; then
    (
      cd "$RELEASE_DIR"
      sha256sum "$checksum_asset_name" | awk '{print $1}'
    )
    return
  fi

  (
    cd "$RELEASE_DIR"
    shasum -a 256 "$checksum_asset_name" | awk '{print $1}'
  )
}

verify_checksum() {
  verified_asset_name="$1"
  checksum_path="$RELEASE_DIR/$verified_asset_name.sha256"

  require_file "$verified_asset_name"
  require_file "$verified_asset_name.sha256"

  set -- $(cat "$checksum_path")
  expected_hash="$1"
  actual_hash=$(sha256_file "$verified_asset_name")

  if [ "$expected_hash" != "$actual_hash" ]; then
    fail "Checksum mismatch for native CLI release asset: $verified_asset_name"
  fi
}

if [ ! -d "$RELEASE_DIR" ]; then
  fail "Native CLI release asset directory does not exist: $RELEASE_DIR"
fi

for asset_name in $EXPECTED_ASSETS; do
  require_file "$asset_name"
done

verify_checksum "uloop-darwin-amd64.tar.gz"
verify_checksum "uloop-darwin-arm64.tar.gz"
verify_checksum "uloop-windows-amd64.zip"

echo "Native CLI release assets are complete."
