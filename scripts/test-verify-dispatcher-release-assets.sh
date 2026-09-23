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
  checksum_dir="${2:-$RELEASE_DIR}"
  if command -v sha256sum >/dev/null 2>&1; then
    (
      cd "$checksum_dir"
      sha256sum "$asset_name" > "$asset_name.sha256"
    )
    return
  fi

  (
    cd "$checksum_dir"
    shasum -a 256 "$asset_name" | awk '{print $1 "  " $2}' > "$asset_name.sha256"
  )
}

write_executable "$RELEASE_DIR/install.sh" "install"
printf '%s\n' "install" > "$RELEASE_DIR/install.ps1"
write_checksum "install.sh"
write_checksum "install.ps1"
write_executable "$PAYLOAD_DIR/uloop" "dispatcher"

tar -czf "$RELEASE_DIR/uloop-dispatcher-darwin-amd64.tar.gz" -C "$PAYLOAD_DIR" ./uloop
tar -czf "$RELEASE_DIR/uloop-dispatcher-darwin-arm64.tar.gz" -C "$PAYLOAD_DIR" ./uloop
tar -czf "$RELEASE_DIR/uloop-dispatcher-linux-amd64.tar.gz" -C "$PAYLOAD_DIR" ./uloop
write_checksum "uloop-dispatcher-darwin-amd64.tar.gz"
write_checksum "uloop-dispatcher-darwin-arm64.tar.gz"
write_checksum "uloop-dispatcher-linux-amd64.tar.gz"

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

LINUX_ARCHIVE="uloop-dispatcher-linux-amd64.tar.gz"

fail() {
  echo "$1" >&2
  exit 1
}

remove_linux_archive() {
  rm "$1/$LINUX_ARCHIVE"
}

corrupt_linux_checksum() {
  printf '%s  %s\n' "0000000000000000000000000000000000000000000000000000000000000000" "$LINUX_ARCHIVE" > "$1/$LINUX_ARCHIVE.sha256"
}

replace_linux_archive_without_executable() {
  case_payload_dir=$(mktemp -d "$TEST_ROOT/payload.XXXXXX")
  printf '%s\n' "unrelated" > "$case_payload_dir/README"
  tar -czf "$1/$LINUX_ARCHIVE" -C "$case_payload_dir" ./README
  write_checksum "$LINUX_ARCHIVE" "$1"
}

# Each case breaks exactly one Linux asset property in a copy of the valid
# fixture, so dropping any single Linux check from the verifier fails a case.
expect_verify_fails() {
  break_asset="$1"
  expected_message="$2"
  case_dir=$(mktemp -d "$TEST_ROOT/case.XXXXXX")
  cp -R "$RELEASE_DIR"/. "$case_dir"/
  "$break_asset" "$case_dir"

  if output=$("$ROOT_DIR/scripts/verify-dispatcher-release-assets.sh" "$case_dir" 2>&1); then
    fail "verify accepted: $break_asset"
  fi

  if ! printf '%s' "$output" | grep -F "$expected_message" >/dev/null; then
    fail "unexpected message for $break_asset: $output"
  fi
}

# The release target resolvers treat --list as the definition of a complete
# release, so a Linux name missing from it would let an incomplete release pass.
expect_listed_asset() {
  listed_asset="$1"
  if ! "$ROOT_DIR/scripts/verify-dispatcher-release-assets.sh" --list | grep -Fx "$listed_asset" >/dev/null; then
    fail "--list does not include $listed_asset"
  fi
}

expect_listed_asset "$LINUX_ARCHIVE"
expect_listed_asset "$LINUX_ARCHIVE.sha256"

expect_verify_fails remove_linux_archive "$LINUX_ARCHIVE"
expect_verify_fails corrupt_linux_checksum "$LINUX_ARCHIVE"
expect_verify_fails replace_linux_archive_without_executable "$LINUX_ARCHIVE"
