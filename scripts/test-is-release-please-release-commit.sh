#!/bin/sh
set -eu

ROOT_DIR=$(CDPATH= cd "$(dirname "$0")/.." && pwd)
SCRIPT="$ROOT_DIR/scripts/is-release-please-release-commit.sh"

assert_release_commit() {
  subject=$1

  if ! "$SCRIPT" "$subject"; then
    echo "Expected release commit subject: $subject" >&2
    exit 1
  fi
}

assert_not_release_commit() {
  subject=$1

  if "$SCRIPT" "$subject"; then
    echo "Expected non-release commit subject: $subject" >&2
    exit 1
  fi
}

# Verifies the scoped release-please commit title used by beta release PRs is detected.
test_detects_scoped_release_commit() {
  assert_release_commit "chore(v3-beta): release 3.0.0-beta.4"
}

# Verifies unscoped release-please commit titles are detected for the main branch.
test_detects_unscoped_release_commit() {
  assert_release_commit "chore: release 1.2.3"
}

# Verifies ordinary feature commits continue to run release-please.
test_rejects_feature_commit() {
  assert_not_release_commit "fix: Setup now upgrades to the native CLI cleanly"
}

# Verifies release-related maintenance commits are not treated as release PR merges.
test_rejects_non_release_chore() {
  assert_not_release_commit "chore(v3-beta): update release notes"
}

test_detects_scoped_release_commit
test_detects_unscoped_release_commit
test_rejects_feature_commit
test_rejects_non_release_chore
