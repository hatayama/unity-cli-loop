#!/bin/sh
set -eu

ROOT_DIR=$(CDPATH= cd "$(dirname "$0")/.." && pwd)
SCRIPT="$ROOT_DIR/scripts/sync-published-release-pr-labels.sh"
TMP_DIR=$(mktemp -d)
ORIGINAL_PATH=$PATH

cleanup() {
  rm -rf "$TMP_DIR"
}

trap cleanup EXIT INT HUP TERM

write_mock_gh() {
  work_dir=$1
  mock_bin="$work_dir/bin"
  mkdir -p "$mock_bin"

  cat > "$mock_bin/gh" <<'MOCK_GH'
#!/bin/sh
set -eu

printf '%s\n' "$*" >> "$GH_LOG"

if [ "$1" = "pr" ] && [ "$2" = "list" ]; then
  printf '%s\n' "$GH_PR_LIST_JSON"
  exit 0
fi

if [ "$1" = "release" ] && [ "$2" = "view" ]; then
  release_tag=$3
  release_json=$(printf '%s\n' "$GH_RELEASES_JSON" | jq -c --arg tag "$release_tag" '.[$tag] // empty')
  if [ -z "$release_json" ]; then
    echo "release not found: $release_tag" >&2
    exit 1
  fi

  printf '%s\n' "$release_json"
  exit 0
fi

if [ "$1" = "pr" ] && [ "$2" = "edit" ]; then
  exit 0
fi

echo "unexpected gh command: $*" >&2
exit 1
MOCK_GH

  chmod +x "$mock_bin/gh"
}

assert_contains() {
  file=$1
  expected=$2

  if ! grep -F "$expected" "$file" >/dev/null; then
    echo "Expected $file to contain: $expected" >&2
    cat "$file" >&2
    exit 1
  fi
}

assert_not_contains() {
  file=$1
  unexpected=$2

  if grep -F "$unexpected" "$file" >/dev/null; then
    echo "Expected $file not to contain: $unexpected" >&2
    cat "$file" >&2
    exit 1
  fi
}

run_case() {
  name=$1
  pr_list_json=$2
  releases_json=$3

  work_dir="$TMP_DIR/$name"
  mkdir -p "$work_dir"
  write_mock_gh "$work_dir"
  touch "$work_dir/gh.log"

  (
    cd "$work_dir"
    set +e
    PATH="$work_dir/bin:$ORIGINAL_PATH" \
      GH_LOG="$work_dir/gh.log" \
      GH_PR_LIST_JSON="$pr_list_json" \
      GH_RELEASES_JSON="$releases_json" \
      TARGET_BRANCH=v3-beta \
      GITHUB_REPOSITORY=hatayama/unity-cli-loop \
      "$SCRIPT" > output.txt 2> stderr.txt
    status=$?
    set -e

    printf '%s\n' "$status" > status.txt
  )
}

# Verifies stale pending labels are repaired when the published release matches the merge commit.
test_marks_stale_pending_release_pr() {
  run_case marks-stale \
    '[{"number":1082,"title":"chore(v3-beta): release 3.0.0-beta.3","mergeCommit":{"oid":"abc123"}}]' \
    '{"v3.0.0-beta.3":{"isDraft":false,"targetCommitish":"abc123"}}'

  assert_contains "$TMP_DIR/marks-stale/status.txt" "0"
  assert_contains "$TMP_DIR/marks-stale/gh.log" "pr edit 1082 --repo hatayama/unity-cli-loop --remove-label autorelease: pending --add-label autorelease: tagged"
  assert_contains "$TMP_DIR/marks-stale/output.txt" "Marked release PR #1082 as tagged for v3.0.0-beta.3."
}

# Verifies draft releases remain pending so release completion is not hidden.
test_keeps_draft_release_pending() {
  run_case draft-release \
    '[{"number":1082,"title":"chore(v3-beta): release 3.0.0-beta.3","mergeCommit":{"oid":"abc123"}}]' \
    '{"v3.0.0-beta.3":{"isDraft":true,"targetCommitish":"abc123"}}'

  assert_contains "$TMP_DIR/draft-release/status.txt" "0"
  assert_contains "$TMP_DIR/draft-release/output.txt" "Pending release PR #1082 does not have a matching published release yet: v3.0.0-beta.3"
  assert_not_contains "$TMP_DIR/draft-release/gh.log" "pr edit"
}

# Verifies release target mismatches remain pending.
test_keeps_mismatched_release_pending() {
  run_case target-mismatch \
    '[{"number":1082,"title":"chore(v3-beta): release 3.0.0-beta.3","mergeCommit":{"oid":"abc123"}}]' \
    '{"v3.0.0-beta.3":{"isDraft":false,"targetCommitish":"different"}}'

  assert_contains "$TMP_DIR/target-mismatch/status.txt" "0"
  assert_contains "$TMP_DIR/target-mismatch/output.txt" "Pending release PR #1082 does not have a matching published release yet: v3.0.0-beta.3"
  assert_not_contains "$TMP_DIR/target-mismatch/gh.log" "pr edit"
}

# Verifies there is no edit when no merged pending release PR exists.
test_exits_when_no_pending_release_pr_exists() {
  run_case no-pending '[]' '{}'

  assert_contains "$TMP_DIR/no-pending/status.txt" "0"
  assert_contains "$TMP_DIR/no-pending/output.txt" "No pending merged release PR labels found for v3-beta."
  assert_not_contains "$TMP_DIR/no-pending/gh.log" "pr edit"
}

test_marks_stale_pending_release_pr
test_keeps_draft_release_pending
test_keeps_mismatched_release_pending
test_exits_when_no_pending_release_pr_exists
