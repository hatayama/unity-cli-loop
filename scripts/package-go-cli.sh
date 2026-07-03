#!/bin/sh
set -eu

ROOT_DIR=$(CDPATH= cd "$(dirname "$0")/.." && pwd)
DIST_DIR="$ROOT_DIR/dist"
RELEASE_DIR="$DIST_DIR/release"

rm -rf "$RELEASE_DIR"
mkdir -p "$RELEASE_DIR"

package_unix() {
  platform="$1"
  tmp_dir="$RELEASE_DIR/tmp-$platform"
  mkdir -p "$tmp_dir"
  cp "$DIST_DIR/$platform/uloop-project-runner" "$tmp_dir/uloop-project-runner"
  chmod +x "$tmp_dir/uloop-project-runner"
  (
    cd "$tmp_dir"
    tar -czf "$RELEASE_DIR/uloop-project-runner-$platform.tar.gz" uloop-project-runner
  )
  rm -rf "$tmp_dir"
}

package_windows() {
  platform="windows-amd64"
  tmp_dir="$RELEASE_DIR/tmp-$platform"
  mkdir -p "$tmp_dir"
  cp "$DIST_DIR/$platform/uloop-project-runner.exe" "$tmp_dir/uloop-project-runner.exe"
  (
    cd "$tmp_dir"
    zip -q "$RELEASE_DIR/uloop-project-runner-$platform.zip" uloop-project-runner.exe
  )
  rm -rf "$tmp_dir"
}

create_checksum() {
  asset_path="$1"
  asset_name=$(basename "$asset_path")
  if command -v sha256sum >/dev/null 2>&1; then
    (
      cd "$RELEASE_DIR"
      sha256sum "$asset_name" > "$asset_name.sha256"
    )
    return
  fi
  (
    cd "$RELEASE_DIR"
    shasum -a 256 "$asset_name" > "$asset_name.sha256"
  )
}

package_unix darwin-arm64
package_unix darwin-amd64
package_windows

for asset_path in "$RELEASE_DIR"/*.tar.gz "$RELEASE_DIR"/*.zip; do
  create_checksum "$asset_path"
done
