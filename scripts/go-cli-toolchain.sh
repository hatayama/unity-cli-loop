#!/bin/sh
set -eu

require_go_cli_toolchain() {
  root_dir="$1"
  version_file="$root_dir/Packages/src/GoCli~/.go-version"

  if [ ! -f "$version_file" ]; then
    echo "Go CLI toolchain version file is missing: $version_file" >&2
    exit 1
  fi

  required_go_version=$(sed -n '1{s/[[:space:]]//g;p;q;}' "$version_file")
  if [ -z "$required_go_version" ]; then
    echo "Go CLI toolchain version file is empty: $version_file" >&2
    exit 1
  fi

  if ! command -v go >/dev/null 2>&1; then
    echo "Go $required_go_version is required to build the Go CLI dist files." >&2
    exit 1
  fi

  actual_go_version=$(go env GOVERSION)
  if [ "$actual_go_version" = "go$required_go_version" ]; then
    return
  fi

  echo "Go $required_go_version is required to build the Go CLI dist files, but found $actual_go_version." >&2
  echo "Use the same Go version as CI before running scripts/check-go-cli.sh or scripts/build-go-cli.sh." >&2
  exit 1
}
