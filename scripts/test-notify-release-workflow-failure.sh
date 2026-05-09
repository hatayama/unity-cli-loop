#!/bin/sh
set -eu

ROOT_DIR=$(CDPATH= cd "$(dirname "$0")/.." && pwd)
SCRIPT="$ROOT_DIR/scripts/notify-release-workflow-failure.sh"
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

if [ "$1" = "label" ] && [ "$2" = "create" ]; then
  exit 0
fi

if [ "$1" = "issue" ] && [ "$2" = "list" ]; then
  if [ "$GH_ISSUE_LIST_FAIL" = "true" ]; then
    exit 1
  fi
  printf '%s\n' "$GH_ISSUE_LIST_JSON"
  exit 0
fi

if [ "$1" = "issue" ] && [ "$2" = "comment" ]; then
  exit 0
fi

if [ "$1" = "issue" ] && [ "$2" = "create" ]; then
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
  issue_list_json=$2
  issue_list_fail=$3

  work_dir="$TMP_DIR/$name"
  mkdir -p "$work_dir"
  write_mock_gh "$work_dir"
  touch "$work_dir/gh.log"

  (
    cd "$work_dir"
    set +e
    PATH="$work_dir/bin:$ORIGINAL_PATH" \
      GH_LOG="$work_dir/gh.log" \
      GH_ISSUE_LIST_JSON="$issue_list_json" \
      GH_ISSUE_LIST_FAIL="$issue_list_fail" \
      WORKFLOW_NAME=native-cli-publish \
      HEAD_BRANCH=v3-beta \
      HEAD_SHA=abc123 \
      RUN_ID=25556818454 \
      RUN_URL=https://example.test/actions/runs/25556818454 \
      CONCLUSION=failure \
      "$SCRIPT" > output.txt 2> stderr.txt
    status=$?
    set -e

    printf '%s\n' "$status" > status.txt
  )
}

# Verifies a new issue is created when no matching release failure issue exists.
test_creates_issue_when_missing() {
  run_case creates-issue '[]' false

  assert_contains "$TMP_DIR/creates-issue/status.txt" "0"
  assert_contains "$TMP_DIR/creates-issue/gh.log" "label create release-failure"
  assert_contains "$TMP_DIR/creates-issue/gh.log" "issue list --state open --label release-failure --limit 1000 --json number,title"
  assert_contains "$TMP_DIR/creates-issue/gh.log" "issue create --title Release workflow failed: native-cli-publish on v3-beta"
  assert_not_contains "$TMP_DIR/creates-issue/gh.log" "issue comment"
}

# Verifies an existing release failure issue receives a new comment.
test_comments_on_existing_issue() {
  run_case comments-existing '[{"number":12,"title":"Release workflow failed: native-cli-publish on v3-beta"}]' false

  assert_contains "$TMP_DIR/comments-existing/status.txt" "0"
  assert_contains "$TMP_DIR/comments-existing/gh.log" "issue comment 12 --body-file"
  assert_not_contains "$TMP_DIR/comments-existing/gh.log" "issue create --title"
}

# Verifies issue lookup failures stop instead of creating duplicate failure issues.
test_issue_lookup_failure_stops() {
  run_case lookup-failure '[]' true

  assert_contains "$TMP_DIR/lookup-failure/status.txt" "1"
  assert_contains "$TMP_DIR/lookup-failure/stderr.txt" "Could not list existing release failure issues."
  assert_not_contains "$TMP_DIR/lookup-failure/gh.log" "issue create --title"
}

test_creates_issue_when_missing
test_comments_on_existing_issue
test_issue_lookup_failure_stops
