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

write_executable "$PAYLOAD_DIR/uloop-project-runner" "real"

tar -czf "$RELEASE_DIR/uloop-project-runner-darwin-amd64.tar.gz" -C "$PAYLOAD_DIR" ./uloop-project-runner
tar -czf "$RELEASE_DIR/uloop-project-runner-darwin-arm64.tar.gz" -C "$PAYLOAD_DIR" ./uloop-project-runner
tar -czf "$RELEASE_DIR/uloop-project-runner-linux-amd64.tar.gz" -C "$PAYLOAD_DIR" ./uloop-project-runner
write_checksum "uloop-project-runner-darwin-amd64.tar.gz"
write_checksum "uloop-project-runner-darwin-arm64.tar.gz"
write_checksum "uloop-project-runner-linux-amd64.tar.gz"

if ! command -v zip >/dev/null 2>&1; then
  echo "zip is required to test native CLI release asset verification" >&2
  exit 1
fi

write_executable "$PAYLOAD_DIR/uloop-project-runner.exe" "real"
(
  cd "$PAYLOAD_DIR"
  zip -q "$RELEASE_DIR/uloop-project-runner-windows-amd64.zip" uloop-project-runner.exe
)
write_checksum "uloop-project-runner-windows-amd64.zip"

"$ROOT_DIR/scripts/verify-native-cli-release-assets.sh" "$RELEASE_DIR"

LINUX_ARCHIVE="uloop-project-runner-linux-amd64.tar.gz"

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

  if output=$("$ROOT_DIR/scripts/verify-native-cli-release-assets.sh" "$case_dir" 2>&1); then
    fail "verify accepted: $break_asset"
  fi

  if ! printf '%s' "$output" | grep -F "$expected_message" >/dev/null; then
    fail "unexpected message for $break_asset: $output"
  fi
}

expect_verify_fails remove_linux_archive "$LINUX_ARCHIVE"
expect_verify_fails corrupt_linux_checksum "$LINUX_ARCHIVE"
expect_verify_fails replace_linux_archive_without_executable "$LINUX_ARCHIVE"
