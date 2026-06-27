#!/bin/sh
set -eu

ROOT_DIR=$(CDPATH= cd "$(dirname "$0")/.." && pwd)
EXPECTED_ASSETS="
install.sh
install.ps1
uloop-dispatcher-darwin-amd64.tar.gz
uloop-dispatcher-darwin-amd64.tar.gz.sha256
uloop-dispatcher-darwin-arm64.tar.gz
uloop-dispatcher-darwin-arm64.tar.gz.sha256
uloop-dispatcher-windows-amd64.zip
uloop-dispatcher-windows-amd64.zip.sha256
"

if [ "${1:-}" = "--list" ]; then
  printf '%s\n' $EXPECTED_ASSETS
  exit 0
fi

RELEASE_DIR="${1:-$ROOT_DIR/cli/dist/dispatcher-release}"

fail() {
  echo "$1" >&2
  exit 1
}

require_file() {
  required_asset_name="$1"
  required_asset_path="$RELEASE_DIR/$required_asset_name"

  if [ ! -f "$required_asset_path" ]; then
    fail "Missing dispatcher release asset: $required_asset_name"
  fi

  if [ ! -s "$required_asset_path" ]; then
    fail "Dispatcher release asset is empty: $required_asset_name"
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
    fail "Checksum mismatch for dispatcher release asset: $verified_asset_name"
  fi
}

require_tar_entry() {
  archive_name="$1"
  entry_name="$2"

  if ! tar -tzf "$RELEASE_DIR/$archive_name" | sed 's#^\./##' | grep -Fx "$entry_name" >/dev/null; then
    fail "Dispatcher release asset $archive_name does not contain $entry_name"
  fi
}

require_zip_entry() {
  archive_name="$1"
  entry_name="$2"

  if ! command -v unzip >/dev/null 2>&1; then
    fail "unzip is required to inspect $archive_name"
  fi

  if ! unzip -Z1 "$RELEASE_DIR/$archive_name" | grep -Fx "$entry_name" >/dev/null; then
    fail "Dispatcher release asset $archive_name does not contain $entry_name"
  fi
}

if [ ! -d "$RELEASE_DIR" ]; then
  fail "Dispatcher release asset directory does not exist: $RELEASE_DIR"
fi

for asset_name in $EXPECTED_ASSETS; do
  require_file "$asset_name"
done

verify_checksum "uloop-dispatcher-darwin-amd64.tar.gz"
verify_checksum "uloop-dispatcher-darwin-arm64.tar.gz"
verify_checksum "uloop-dispatcher-windows-amd64.zip"

require_tar_entry "uloop-dispatcher-darwin-amd64.tar.gz" "uloop"
require_tar_entry "uloop-dispatcher-darwin-arm64.tar.gz" "uloop"
require_zip_entry "uloop-dispatcher-windows-amd64.zip" "uloop.exe"

echo "Dispatcher release assets are complete."
