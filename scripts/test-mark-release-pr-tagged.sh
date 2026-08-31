#!/bin/sh
set -eu

ROOT_DIR=$(CDPATH= cd "$(dirname "$0")/.." && pwd)
SCRIPT="$ROOT_DIR/scripts/mark-release-pr-tagged.sh"
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

if [ "$1" = "release" ] && [ "$2" = "view" ]; then
  printf '%s\n' "$GH_RELEASE_JSON"
  exit 0
fi

if [ "$1" = "pr" ] && [ "$2" = "list" ]; then
  printf '%s\n' "$GH_PR_LIST_JSON"
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

assert_file_equals() {
  file=$1
  expected=$2
  actual=$(cat "$file")

  if [ "$actual" != "$expected" ]; then
    echo "Expected $file to equal: $expected" >&2
    echo "Actual content:" >&2
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
  release_json=$2
  pr_list_json=$3

  work_dir="$TMP_DIR/$name"
  mkdir -p "$work_dir"
  write_mock_gh "$work_dir"
  touch "$work_dir/gh.log"

  (
    cd "$work_dir"
    set +e
    PATH="$work_dir/bin:$ORIGINAL_PATH" \
      GH_LOG="$work_dir/gh.log" \
      GH_RELEASE_JSON="$release_json" \
      GH_PR_LIST_JSON="$pr_list_json" \
      RELEASE_TAG=v3.0.0-beta.3 \
      TARGET_SHA=abc123 \
      TARGET_BRANCH=v3-beta \
      GITHUB_REPOSITORY=hatayama/unity-cli-loop \
      "$SCRIPT" > output.txt 2> stderr.txt
    status=$?
    set -e

    printf '%s\n' "$status" > status.txt
  )
}

# Verifies a published release moves the matching release PR from pending to tagged.
test_marks_matching_release_pr() {
  run_case marks-pr \
    '{"isDraft":false,"targetCommitish":"abc123"}' \
    '[{"number":1082,"title":"chore(v3-beta): release 3.0.0-beta.3","mergeCommit":{"oid":"abc123"}}]'

  assert_file_equals "$TMP_DIR/marks-pr/status.txt" "0"
  assert_contains "$TMP_DIR/marks-pr/gh.log" "pr edit 1082 --repo hatayama/unity-cli-loop --remove-label autorelease: pending --add-label autorelease: tagged"
  assert_contains "$TMP_DIR/marks-pr/output.txt" "Marked release PR #1082 as tagged for v3.0.0-beta.3."
}

# Verifies draft releases do not update release PR labels.
test_skips_draft_release() {
  run_case draft-release \
    '{"isDraft":true,"targetCommitish":"abc123"}' \
    '[{"number":1082,"title":"chore(v3-beta): release 3.0.0-beta.3","mergeCommit":{"oid":"abc123"}}]'

  assert_file_equals "$TMP_DIR/draft-release/status.txt" "0"
  assert_contains "$TMP_DIR/draft-release/output.txt" "Release v3.0.0-beta.3 is still draft; leaving release PR labels unchanged."
  assert_not_contains "$TMP_DIR/draft-release/gh.log" "pr edit"
}

# Verifies release target mismatches fail before editing labels.
test_fails_on_release_target_mismatch() {
  run_case target-mismatch \
    '{"isDraft":false,"targetCommitish":"different"}' \
    '[{"number":1082,"title":"chore(v3-beta): release 3.0.0-beta.3","mergeCommit":{"oid":"abc123"}}]'

  assert_file_equals "$TMP_DIR/target-mismatch/status.txt" "1"
  assert_contains "$TMP_DIR/target-mismatch/stderr.txt" "Release v3.0.0-beta.3 points at different, expected abc123."
  assert_not_contains "$TMP_DIR/target-mismatch/gh.log" "pr edit"
}

# Verifies missing pending PRs are treated as already settled.
test_skips_when_pending_release_pr_is_missing() {
  run_case missing-pr \
    '{"isDraft":false,"targetCommitish":"abc123"}' \
    '[]'

  assert_file_equals "$TMP_DIR/missing-pr/status.txt" "0"
  assert_contains "$TMP_DIR/missing-pr/output.txt" "No pending release PR found for v3.0.0-beta.3 at abc123."
  assert_not_contains "$TMP_DIR/missing-pr/gh.log" "pr edit"
}

test_marks_matching_release_pr
test_skips_draft_release
test_fails_on_release_target_mismatch
test_skips_when_pending_release_pr_is_missing
