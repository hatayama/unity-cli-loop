#!/bin/sh
set -eu

ROOT_DIR=$(CDPATH= cd "$(dirname "$0")/.." && pwd)
CORE_DIR="$ROOT_DIR/Packages/src/Cli~/Core~"
DISPATCHER_DIR="$ROOT_DIR/Packages/src/Cli~/Dispatcher~"

. "$ROOT_DIR/scripts/go-cli-toolchain.sh"
require_go_cli_toolchain "$ROOT_DIR"

build_binary() {
  os="$1"
  arch="$2"
  name="$3"
  module_dir="$4"
  package="$5"
  extension=""

  if [ "$os" = "windows" ]; then
    extension=".exe"
  fi

  output_dir="$module_dir/dist/$os-$arch"
  mkdir -p "$output_dir"

  (
    cd "$module_dir"
    GOOS="$os" GOARCH="$arch" CGO_ENABLED=0 go build -trimpath -buildvcs=false -ldflags="-s -w" -o "$output_dir/$name$extension" "$package"
  )
}

build_binary darwin arm64 uloop-core "$CORE_DIR" ./cmd/uloop-core
build_binary darwin amd64 uloop-core "$CORE_DIR" ./cmd/uloop-core
build_binary windows amd64 uloop-core "$CORE_DIR" ./cmd/uloop-core

build_binary darwin arm64 uloop-dispatcher "$DISPATCHER_DIR" ./cmd/uloop-dispatcher
build_binary darwin amd64 uloop-dispatcher "$DISPATCHER_DIR" ./cmd/uloop-dispatcher
build_binary windows amd64 uloop-dispatcher "$DISPATCHER_DIR" ./cmd/uloop-dispatcher
