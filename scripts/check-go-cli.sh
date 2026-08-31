#!/bin/sh
set -eu

ROOT_DIR=$(CDPATH= cd "$(dirname "$0")/.." && pwd)

"$ROOT_DIR/scripts/check-go-cli-source.sh"
"$ROOT_DIR/scripts/verify-go-cli-dist.sh"
