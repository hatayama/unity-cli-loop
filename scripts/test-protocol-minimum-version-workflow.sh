#!/bin/sh
set -eu

ROOT_DIR=$(CDPATH= cd "$(dirname "$0")/.." && pwd)
BUILD_WORKFLOW="$ROOT_DIR/.github/workflows/build-and-test.yml"
COMMENT_WORKFLOW="$ROOT_DIR/.github/workflows/protocol-minimum-version-warning.yml"

assert_contains() {
  file=$1
  expected=$2
  if ! grep -F -- "$expected" "$file" >/dev/null 2>&1; then
    echo "Expected $file to contain: $expected" >&2
    exit 1
  fi
}

assert_file_exists() {
  file=$1
  if [ ! -f "$file" ]; then
    echo "Expected file to exist: $file" >&2
    exit 1
  fi
}

test_pull_request_guard_fails_on_minimum_version_omission() {
  assert_contains "$BUILD_WORKFLOW" "      - name: Check protocol minimum version bump"
  assert_contains "$BUILD_WORKFLOW" "        if: github.event_name == 'pull_request'"
  assert_contains "$BUILD_WORKFLOW" '        run: go run ./cmd/check-protocol-minimum-version --base "origin/${{ github.base_ref }}" --head HEAD'
}

test_pull_request_target_comment_workflow_is_trusted() {
  assert_file_exists "$COMMENT_WORKFLOW"
  assert_contains "$COMMENT_WORKFLOW" "name: Protocol Minimum Version Warning"
  assert_contains "$COMMENT_WORKFLOW" "  pull_request_target:"
  assert_contains "$COMMENT_WORKFLOW" "  contents: read"
  assert_contains "$COMMENT_WORKFLOW" "  issues: write"
  assert_contains "$COMMENT_WORKFLOW" "  pull-requests: read"
  assert_contains "$COMMENT_WORKFLOW" "      - name: Checkout base repository"
  assert_contains "$COMMENT_WORKFLOW" "      - name: Fetch pull request head"
  assert_contains "$COMMENT_WORKFLOW" '        run: git fetch --no-tags origin "pull/${{ github.event.pull_request.number }}/head:protocol-minimum-version-pr-head"'
  assert_contains "$COMMENT_WORKFLOW" "      - name: Comment on protocol minimum version guard"
  assert_contains "$COMMENT_WORKFLOW" "          PROTOCOL_MINIMUM_VERSION_HEAD_REF: protocol-minimum-version-pr-head"
  assert_contains "$COMMENT_WORKFLOW" "        run: go run ./cmd/comment-protocol-minimum-version"
}

test_pull_request_guard_fails_on_minimum_version_omission
test_pull_request_target_comment_workflow_is_trusted
