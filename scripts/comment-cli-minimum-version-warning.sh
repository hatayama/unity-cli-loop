#!/bin/sh
set -eu

ROOT_DIR=${ULOOP_REPOSITORY_ROOT:-$(CDPATH= cd "$(dirname "$0")/.." && pwd)}
MINIMUM_VERSION_FILE="Packages/src/Editor/Domain/CliConstants.cs"
MARKER="<!-- uloop-cli-minimum-version-warning -->"
PR_NUMBER=${PR_NUMBER:-}
REPOSITORY=${GITHUB_REPOSITORY:-}
BASE_REF=${CLI_MINIMUM_VERSION_BASE_REF:-}

if [ -z "$PR_NUMBER" ]; then
  echo "Skipping CLI minimum version comment because no PR number was provided."
  exit 0
fi

if [ -z "$REPOSITORY" ]; then
  REPOSITORY=$(gh repo view --json nameWithOwner --jq .nameWithOwner)
fi

if [ -z "$BASE_REF" ] && [ -n "${GITHUB_BASE_REF:-}" ]; then
  BASE_REF="origin/$GITHUB_BASE_REF"
fi

if [ -z "$BASE_REF" ]; then
  echo "Skipping CLI minimum version comment because no base ref was provided."
  exit 0
fi

CHANGED_FILES=$(git -C "$ROOT_DIR" diff --name-only "$BASE_REF...HEAD" --)

has_changed_file() {
  expected=$1

  for changed_file in $CHANGED_FILES; do
    if [ "$changed_file" = "$expected" ]; then
      return 0
    fi
  done

  return 1
}

has_go_cli_change() {
  for changed_file in $CHANGED_FILES; do
    case "$changed_file" in
      Packages/src/Cli~/CHANGELOG.md|Packages/src/Cli~/dist/*)
        ;;
      Packages/src/Cli~/*.go|Packages/src/Cli~/go.mod|Packages/src/Cli~/go.sum|Packages/src/Cli~/contract.json|Packages/src/Cli~/layout-contract.json|Packages/src/Cli~/cmd/*|Packages/src/Cli~/internal/*)
        return 0
        ;;
    esac
  done

  return 1
}

existing_comment_id() {
  gh api --paginate "repos/$REPOSITORY/issues/$PR_NUMBER/comments" \
    --jq ".[] | select(.body | contains(\"$MARKER\")) | .id" |
    tail -n 1
}

write_body_json() {
  body_file=$1
  json_file=$2

  jq -n --rawfile body "$body_file" '{body: $body}' > "$json_file"
}

upsert_comment() {
  body_file=$1
  json_file=$(mktemp)
  comment_id=$(existing_comment_id)

  write_body_json "$body_file" "$json_file"

  if [ -n "$comment_id" ]; then
    gh api --method PATCH "repos/$REPOSITORY/issues/comments/$comment_id" --input "$json_file" >/dev/null
    rm -f "$json_file"
    echo "Updated CLI minimum version comment."
    return
  fi

  gh api --method POST "repos/$REPOSITORY/issues/$PR_NUMBER/comments" --input "$json_file" >/dev/null
  rm -f "$json_file"
  echo "Posted CLI minimum version comment."
}

update_existing_comment() {
  body_file=$1
  json_file=$(mktemp)
  comment_id=$(existing_comment_id)

  if [ -z "$comment_id" ]; then
    return
  fi

  write_body_json "$body_file" "$json_file"
  gh api --method PATCH "repos/$REPOSITORY/issues/comments/$comment_id" --input "$json_file" >/dev/null
  rm -f "$json_file"
  echo "Resolved CLI minimum version comment."
}

WARNING_BODY=$(mktemp)
RESOLVED_BODY=$(mktemp)

cleanup() {
  rm -f "$WARNING_BODY" "$RESOLVED_BODY"
}

trap cleanup EXIT INT HUP TERM

cat > "$WARNING_BODY" <<EOF_WARNING
$MARKER
Warning: Go CLI files changed, but \`MINIMUM_REQUIRED_CLI_VERSION\` was not updated.

Please confirm whether the Unity package can still accept older CLI versions. If the package now depends on new CLI behavior, update \`Packages/src/Editor/Domain/CliConstants.cs\`.
EOF_WARNING

cat > "$RESOLVED_BODY" <<EOF_RESOLVED
$MARKER
Resolved: this PR no longer has Go CLI changes without a \`MINIMUM_REQUIRED_CLI_VERSION\` update.
EOF_RESOLVED

if has_go_cli_change && ! has_changed_file "$MINIMUM_VERSION_FILE"; then
  upsert_comment "$WARNING_BODY"
  exit 0
fi

update_existing_comment "$RESOLVED_BODY"
