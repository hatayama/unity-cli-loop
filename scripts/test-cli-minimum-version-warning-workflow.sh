#!/bin/sh
set -eu

ROOT_DIR=$(CDPATH= cd "$(dirname "$0")/.." && pwd)
WARNING_WORKFLOW="$ROOT_DIR/.github/workflows/cli-minimum-version-warning.yml"
BUILD_WORKFLOW="$ROOT_DIR/.github/workflows/build-and-test.yml"

assert_contains() {
  file=$1
  expected=$2

  if ! grep -F -- "$expected" "$file" >/dev/null 2>&1; then
    echo "Expected $file to contain: $expected" >&2
    exit 1
  fi
}

assert_not_contains() {
  file=$1
  unexpected=$2

  if grep -F -- "$unexpected" "$file" >/dev/null 2>&1; then
    echo "Expected $file not to contain: $unexpected" >&2
    exit 1
  fi
}

assert_contains "$WARNING_WORKFLOW" "  pull_request_target:"
assert_contains "$WARNING_WORKFLOW" "  issues: write"
assert_contains "$WARNING_WORKFLOW" "      - name: Setup Go"
assert_contains "$WARNING_WORKFLOW" "        uses: actions/setup-go@4a3601121dd01d1626a1e23e37211e3254c1c06c"
assert_contains "$WARNING_WORKFLOW" "          cache: false"
assert_contains "$WARNING_WORKFLOW" "      - name: Fetch pull request head"
assert_contains "$WARNING_WORKFLOW" "          CLI_MINIMUM_VERSION_HEAD_REF: cli-minimum-version-pr-head"
assert_contains "$WARNING_WORKFLOW" "        run: scripts/comment-cli-minimum-version-warning.sh"

assert_not_contains "$BUILD_WORKFLOW" "      - name: Comment on CLI minimum version check"
assert_not_contains "$BUILD_WORKFLOW" "          PR_NUMBER: \${{ github.event.pull_request.number }}"
