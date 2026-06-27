#!/bin/sh
set -eu

ROOT_DIR=$(CDPATH= cd "$(dirname "$0")/.." && pwd)
TEST_ROOT=$(mktemp -d)

cleanup() {
  rm -rf "$TEST_ROOT"
}
trap cleanup EXIT

RELEASE_DIR="$TEST_ROOT/release"
PAYLOAD_DIR="$TEST_ROOT/payload"
mkdir -p "$RELEASE_DIR" "$PAYLOAD_DIR"

write_executable() {
  path="$1"
  content="$2"
  printf '%s\n' "$content" > "$path"
  chmod +x "$path"
}

write_checksum() {
  asset_name="$1"
  if command -v sha256sum >/dev/null 2>&1; then
    (
      cd "$RELEASE_DIR"
      sha256sum "$asset_name" > "$asset_name.sha256"
    )
    return
  fi

  (
    cd "$RELEASE_DIR"
    shasum -a 256 "$asset_name" | awk '{print $1 "  " $2}' > "$asset_name.sha256"
  )
}

write_executable "$RELEASE_DIR/install.sh" "install"
printf '%s\n' "install" > "$RELEASE_DIR/install.ps1"
write_executable "$PAYLOAD_DIR/uloop" "dispatcher"

tar -czf "$RELEASE_DIR/uloop-dispatcher-darwin-amd64.tar.gz" -C "$PAYLOAD_DIR" ./uloop
tar -czf "$RELEASE_DIR/uloop-dispatcher-darwin-arm64.tar.gz" -C "$PAYLOAD_DIR" ./uloop
write_checksum "uloop-dispatcher-darwin-amd64.tar.gz"
write_checksum "uloop-dispatcher-darwin-arm64.tar.gz"

if ! command -v zip >/dev/null 2>&1; then
  echo "zip is required to test dispatcher release asset verification" >&2
  exit 1
fi

write_executable "$PAYLOAD_DIR/uloop.exe" "dispatcher"
(
  cd "$PAYLOAD_DIR"
  zip -q "$RELEASE_DIR/uloop-dispatcher-windows-amd64.zip" uloop.exe
)
write_checksum "uloop-dispatcher-windows-amd64.zip"

"$ROOT_DIR/scripts/verify-dispatcher-release-assets.sh" "$RELEASE_DIR"
