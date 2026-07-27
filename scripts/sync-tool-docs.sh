#!/bin/sh
# Regenerate cli/common/tools/default-tools.json descriptions from the package's SKILL.md parameter
# tables. Pass --check to verify instead of write, which is what CI runs.
set -eu

ROOT_DIR=$(CDPATH= cd "$(dirname "$0")/.." && pwd)

cd "$ROOT_DIR/cli/release-automation"
exec go run ./cmd/sync-tool-docs --repository-root "$ROOT_DIR" "$@"
