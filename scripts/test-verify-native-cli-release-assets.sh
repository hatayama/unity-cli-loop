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

write_executable "$PAYLOAD_DIR/uloop-cli" "real"
write_executable "$PAYLOAD_DIR/uloop" "dispatcher"

tar -czf "$RELEASE_DIR/uloop-cli-darwin-amd64.tar.gz" -C "$PAYLOAD_DIR" ./uloop-cli
tar -czf "$RELEASE_DIR/uloop-cli-darwin-arm64.tar.gz" -C "$PAYLOAD_DIR" ./uloop-cli
tar -czf "$RELEASE_DIR/uloop-darwin-amd64.tar.gz" -C "$PAYLOAD_DIR" ./uloop ./uloop-cli
tar -czf "$RELEASE_DIR/uloop-darwin-arm64.tar.gz" -C "$PAYLOAD_DIR" ./uloop ./uloop-cli
write_checksum "uloop-cli-darwin-amd64.tar.gz"
write_checksum "uloop-cli-darwin-arm64.tar.gz"
write_checksum "uloop-darwin-amd64.tar.gz"
write_checksum "uloop-darwin-arm64.tar.gz"

if ! command -v zip >/dev/null 2>&1; then
  echo "zip is required to test native CLI release asset verification" >&2
  exit 1
fi

write_executable "$PAYLOAD_DIR/uloop-cli.exe" "real"
write_executable "$PAYLOAD_DIR/uloop.exe" "dispatcher"
(
  cd "$PAYLOAD_DIR"
  zip -q "$RELEASE_DIR/uloop-cli-windows-amd64.zip" uloop-cli.exe
  zip -q "$RELEASE_DIR/uloop-windows-amd64.zip" uloop.exe uloop-cli.exe
)
write_checksum "uloop-cli-windows-amd64.zip"
write_checksum "uloop-windows-amd64.zip"

"$ROOT_DIR/scripts/verify-native-cli-release-assets.sh" "$RELEASE_DIR"
