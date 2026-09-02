#!/bin/sh
set -eu

ROOT_DIR=$(CDPATH= cd "$(dirname "$0")/.." && pwd)
RUNNER_DIR="$ROOT_DIR/cli/project-runner"
DISPATCHER_DIR="$ROOT_DIR/cli/dispatcher"
GO_WINRES_MODULE="github.com/tc-hib/go-winres@v0.3.3"

. "$ROOT_DIR/scripts/go-cli-toolchain.sh"
require_go_cli_toolchain "$ROOT_DIR"

# stripped and unsigned Go binaries without VERSIONINFO are a strong "unknown software"
# signal for Microsoft Defender's ML classifier (Issue #2503), so the Windows builds
# carry version resources generated from the release version.
generate_windows_resources() {
  module_dir="$1"
  main_package_dir="$2"
  version="$3"

  (
    cd "$module_dir"
    go run "$GO_WINRES_MODULE" make --in "$main_package_dir/winres/winres.json" --arch amd64 --file-version "$version" --product-version "$version" --out "$main_package_dir/rsrc"
  )
}

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

  output_dir="$ROOT_DIR/dist/$os-$arch"
  mkdir -p "$output_dir"

  (
    cd "$module_dir"
    GOOS="$os" GOARCH="$arch" CGO_ENABLED=0 go build -trimpath -buildvcs=false -ldflags="-s -w" -o "$output_dir/$name$extension" "$package"
  )
}

DISPATCHER_VERSION=$(jq -r '.dispatcherVersion' "$DISPATCHER_DIR/dispatchercontract/dispatcher-contract.json")
if [ -z "$DISPATCHER_VERSION" ] || [ "$DISPATCHER_VERSION" = "null" ]; then
  echo "Could not resolve dispatcherVersion from $DISPATCHER_DIR/dispatchercontract/dispatcher-contract.json." >&2
  exit 1
fi

PROJECT_RUNNER_VERSION=$(jq -r '.projectRunnerVersion' "$ROOT_DIR/cli/common/clicontract/contract.json")
if [ -z "$PROJECT_RUNNER_VERSION" ] || [ "$PROJECT_RUNNER_VERSION" = "null" ]; then
  echo "Could not resolve projectRunnerVersion from $ROOT_DIR/cli/common/clicontract/contract.json." >&2
  exit 1
fi

generate_windows_resources "$DISPATCHER_DIR" "$DISPATCHER_DIR/cmd/dispatcher" "$DISPATCHER_VERSION"
generate_windows_resources "$RUNNER_DIR" "$RUNNER_DIR/cmd/project-runner" "$PROJECT_RUNNER_VERSION"

build_binary darwin arm64 uloop "$DISPATCHER_DIR" ./cmd/dispatcher
build_binary darwin arm64 uloop-project-runner "$RUNNER_DIR" ./cmd/project-runner
build_binary darwin amd64 uloop "$DISPATCHER_DIR" ./cmd/dispatcher
build_binary darwin amd64 uloop-project-runner "$RUNNER_DIR" ./cmd/project-runner
build_binary windows amd64 uloop "$DISPATCHER_DIR" ./cmd/dispatcher
build_binary windows amd64 uloop-project-runner "$RUNNER_DIR" ./cmd/project-runner
