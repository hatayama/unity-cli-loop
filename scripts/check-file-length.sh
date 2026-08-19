#!/bin/sh
set -eu

ROOT_DIR=$(CDPATH= cd "$(dirname "$0")/.." && pwd)
MAX_FILE_LENGTH=${CODE_FILE_LENGTH_MAX_LENGTH:-500}
FAIL_ON_EXCEEDED=$(printf '%s' "${CODE_FILE_LENGTH_FAIL_ON_EXCEEDED:-false}" | tr '[:upper:]' '[:lower:]')

(
  cd "$ROOT_DIR/cli/release-automation"
  go run ./cmd/check-file-length --root "$ROOT_DIR" --max-length "$MAX_FILE_LENGTH" --fail-on-exceeded "$FAIL_ON_EXCEEDED"
)
