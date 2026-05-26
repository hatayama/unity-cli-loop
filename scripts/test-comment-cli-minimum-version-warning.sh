#!/bin/sh
set -eu

ROOT_DIR=$(CDPATH= cd "$(dirname "$0")/.." && pwd)
SCRIPT="$ROOT_DIR/scripts/comment-cli-minimum-version-warning.sh"
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

method=GET
path=
input=

while [ "$#" -gt 0 ]; do
  case "$1" in
    api)
      ;;
    --method)
      shift
      method=$1
      ;;
    --input)
      shift
      input=$1
      ;;
    --jq)
      shift
      ;;
    --*)
      ;;
    *)
      if [ -z "$path" ]; then
        path=$1
      fi
      ;;
  esac
  shift
done

printf '%s %s\n' "$method" "$path" >> "$GH_LOG"

if [ "$method" = "GET" ]; then
  if [ -n "$GH_EXISTING_COMMENT_ID" ]; then
    printf '%s\n' "$GH_EXISTING_COMMENT_ID"
  fi
  exit 0
fi

if [ -n "$input" ]; then
  cat "$input" >> "$GH_BODY_LOG"
fi
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

write_file() {
  file=$1
  content=$2

  mkdir -p "$(dirname "$file")"
  printf '%s\n' "$content" > "$file"
}

create_repository() {
  work_dir=$1

  git init -q "$work_dir/repo"
  (
    cd "$work_dir/repo"
    git config user.email test@example.invalid
    git config user.name "Test User"
    write_file Packages/src/Cli~/internal/cli/run.go "package cli"
    write_file Packages/src/Editor/Domain/CliConstants.cs "public const string MINIMUM_REQUIRED_CLI_VERSION = \"3.0.0-beta.14\";"
    git add .
    git commit -q -m base
  )
}

run_case() {
  name=$1
  existing_comment_id=$2
  mutation=$3

  work_dir="$TMP_DIR/$name"
  mkdir -p "$work_dir"
  write_mock_gh "$work_dir"
  touch "$work_dir/gh.log" "$work_dir/body.log"
  create_repository "$work_dir"

  (
    cd "$work_dir/repo"
    case "$mutation" in
      go-cli)
        write_file Packages/src/Cli~/internal/cli/run.go "package cli // changed"
        ;;
      go-cli-and-minimum)
        write_file Packages/src/Cli~/internal/cli/run.go "package cli // changed"
        write_file Packages/src/Editor/Domain/CliConstants.cs "public const string MINIMUM_REQUIRED_CLI_VERSION = \"3.0.0-beta.15\";"
        ;;
      docs)
        write_file README.md "documentation"
        ;;
    esac
    git add .
    git commit -q -m "$mutation"

    PATH="$work_dir/bin:$ORIGINAL_PATH" \
      GH_LOG="$work_dir/gh.log" \
      GH_BODY_LOG="$work_dir/body.log" \
      GH_EXISTING_COMMENT_ID="$existing_comment_id" \
      ULOOP_REPOSITORY_ROOT="$work_dir/repo" \
      PR_NUMBER=123 \
      GITHUB_REPOSITORY=hatayama/unity-cli-loop \
      CLI_MINIMUM_VERSION_BASE_REF=HEAD^ \
      "$SCRIPT" > "$work_dir/output.txt"
  )
}

run_head_ref_case() {
  name=$1

  work_dir="$TMP_DIR/$name"
  mkdir -p "$work_dir"
  write_mock_gh "$work_dir"
  touch "$work_dir/gh.log" "$work_dir/body.log"
  create_repository "$work_dir"

  (
    cd "$work_dir/repo"
    base_branch=$(git branch --show-current)
    git switch -q -c pr-head
    write_file Packages/src/Cli~/internal/cli/run.go "package cli // changed"
    git add .
    git commit -q -m go-cli
    git switch -q "$base_branch"

    PATH="$work_dir/bin:$ORIGINAL_PATH" \
      GH_LOG="$work_dir/gh.log" \
      GH_BODY_LOG="$work_dir/body.log" \
      GH_EXISTING_COMMENT_ID="" \
      ULOOP_REPOSITORY_ROOT="$work_dir/repo" \
      PR_NUMBER=123 \
      GITHUB_REPOSITORY=hatayama/unity-cli-loop \
      CLI_MINIMUM_VERSION_BASE_REF=HEAD \
      CLI_MINIMUM_VERSION_HEAD_REF=pr-head \
      "$SCRIPT" > "$work_dir/output.txt"
  )
}

# Verifies a Go CLI change without a minimum-version change creates a warning comment.
run_case posts-warning "" go-cli
assert_contains "$TMP_DIR/posts-warning/gh.log" "POST repos/hatayama/unity-cli-loop/issues/123/comments"
assert_contains "$TMP_DIR/posts-warning/body.log" "Go CLI files changed"

# Verifies pull_request_target can diff a fetched PR head without checking it out.
run_head_ref_case posts-warning-from-head-ref
assert_contains "$TMP_DIR/posts-warning-from-head-ref/gh.log" "POST repos/hatayama/unity-cli-loop/issues/123/comments"
assert_contains "$TMP_DIR/posts-warning-from-head-ref/body.log" "Go CLI files changed"

# Verifies a matching existing comment is updated instead of duplicated.
run_case updates-warning 456 go-cli
assert_contains "$TMP_DIR/updates-warning/gh.log" "PATCH repos/hatayama/unity-cli-loop/issues/comments/456"
assert_contains "$TMP_DIR/updates-warning/body.log" "Go CLI files changed"
assert_not_contains "$TMP_DIR/updates-warning/gh.log" "POST"

# Verifies a minimum-version update resolves the existing reminder.
run_case resolves-existing-warning 456 go-cli-and-minimum
assert_contains "$TMP_DIR/resolves-existing-warning/gh.log" "PATCH repos/hatayama/unity-cli-loop/issues/comments/456"
assert_contains "$TMP_DIR/resolves-existing-warning/body.log" "Resolved:"

# Verifies unrelated changes do not create a new comment.
run_case ignores-docs "" docs
assert_not_contains "$TMP_DIR/ignores-docs/gh.log" "POST"
assert_not_contains "$TMP_DIR/ignores-docs/gh.log" "PATCH"

# Verifies non-PR runs do not call GitHub.
work_dir="$TMP_DIR/no-pr"
mkdir -p "$work_dir"
write_mock_gh "$work_dir"
touch "$work_dir/gh.log" "$work_dir/body.log"
create_repository "$work_dir"
(
  cd "$work_dir/repo"
  PATH="$work_dir/bin:$ORIGINAL_PATH" \
    GH_LOG="$work_dir/gh.log" \
    GH_BODY_LOG="$work_dir/body.log" \
    ULOOP_REPOSITORY_ROOT="$work_dir/repo" \
    "$SCRIPT" > "$work_dir/output.txt"
)
assert_contains "$work_dir/output.txt" "no PR number"
assert_not_contains "$work_dir/gh.log" "repos/"
