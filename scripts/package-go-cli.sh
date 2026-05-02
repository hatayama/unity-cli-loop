#!/bin/sh
set -eu

ROOT_DIR=$(CDPATH= cd "$(dirname "$0")/.." && pwd)
GO_CLI_DIR="$ROOT_DIR/Packages/src/GoCli~"
DIST_DIR="$GO_CLI_DIR/dist"
RELEASE_DIR="$GO_CLI_DIR/release"

rm -rf "$RELEASE_DIR"
mkdir -p "$RELEASE_DIR"

package_unix() {
  platform="$1"
  tmp_dir="$RELEASE_DIR/tmp-$platform"
  mkdir -p "$tmp_dir"
  cp "$DIST_DIR/$platform/uloop-dispatcher" "$tmp_dir/uloop"
  chmod +x "$tmp_dir/uloop"
  (
    cd "$tmp_dir"
    tar -czf "$RELEASE_DIR/uloop-$platform.tar.gz" uloop
  )
  rm -rf "$tmp_dir"
}

package_windows() {
  platform="windows-amd64"
  tmp_dir="$RELEASE_DIR/tmp-$platform"
  mkdir -p "$tmp_dir"
  cp "$DIST_DIR/$platform/uloop-dispatcher.exe" "$tmp_dir/uloop.exe"
  (
    cd "$tmp_dir"
    zip -q "$RELEASE_DIR/uloop-$platform.zip" uloop.exe
  )
  rm -rf "$tmp_dir"
}

package_installers() {
  cp "$ROOT_DIR/scripts/install.sh" "$RELEASE_DIR/install.sh"
  cp "$ROOT_DIR/scripts/install.ps1" "$RELEASE_DIR/install.ps1"
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
package_installers

for asset_path in "$RELEASE_DIR"/*.tar.gz "$RELEASE_DIR"/*.zip; do
  create_checksum "$asset_path"
done
