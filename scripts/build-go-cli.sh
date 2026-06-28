#!/bin/sh
set -eu

ROOT_DIR=$(CDPATH= cd "$(dirname "$0")/.." && pwd)
CLI_DIR="$ROOT_DIR/cli"

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

  output_dir="$CLI_DIR/dist/$os-$arch"
  mkdir -p "$output_dir"

  (
    cd "$module_dir"
    GOOS="$os" GOARCH="$arch" CGO_ENABLED=0 go build -trimpath -buildvcs=false -ldflags="-s -w" -o "$output_dir/$name$extension" "$package"
  )
}

build_binary darwin arm64 uloop "$CLI_DIR" ./cmd/dispatcher
build_binary darwin arm64 uloop-project-runner "$CLI_DIR" ./cmd/project-runner
build_binary darwin amd64 uloop "$CLI_DIR" ./cmd/dispatcher
build_binary darwin amd64 uloop-project-runner "$CLI_DIR" ./cmd/project-runner
build_binary windows amd64 uloop "$CLI_DIR" ./cmd/dispatcher
build_binary windows amd64 uloop-project-runner "$CLI_DIR" ./cmd/project-runner
