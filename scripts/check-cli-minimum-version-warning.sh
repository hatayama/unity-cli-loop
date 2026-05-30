#!/bin/sh
set -eu

ROOT_DIR=${ULOOP_REPOSITORY_ROOT:-$(CDPATH= cd "$(dirname "$0")/.." && pwd)}

CLI_MINIMUM_VERSION_FAIL_ON_WARNING=true
export CLI_MINIMUM_VERSION_FAIL_ON_WARNING

ULOOP_REPOSITORY_ROOT="$ROOT_DIR" "$ROOT_DIR/scripts/comment-cli-minimum-version-warning.sh"
