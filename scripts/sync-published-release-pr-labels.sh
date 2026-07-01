#!/bin/sh
set -eu

: "${TARGET_BRANCH:?TARGET_BRANCH is required}"

REPO_FULL_NAME=${GITHUB_REPOSITORY:-hatayama/unity-cli-loop}
PENDING_LABEL="autorelease: pending"
TAGGED_LABEL="autorelease: tagged"

release_version_from_title() {
  title=$1

  printf '%s\n' "$title" | jq -R -r '
    try capture("^chore(\\([^)]*\\))?: release (?<version>[0-9][A-Za-z0-9._-]*)$").version catch ""
  '
}

release_version_from_body() {
  body=$1

  printf '%s\n' "$body" | jq -R -s -r '
    try capture("<summary>(?:[^<:]+:\\s*)?(?<version>[0-9][A-Za-z0-9._-]*)</summary>").version catch ""
  '
}

release_version_from_pr() {
  title=$1
  body=$2
  release_version=$(release_version_from_title "$title")

  if [ -n "$release_version" ]; then
    printf '%s\n' "$release_version"
    return
  fi

  release_version_from_body "$body"
}

release_is_published_at_sha() {
  release_tag=$1
  expected_sha=$2
  release_error_file=$(mktemp)

  if ! release_json=$(gh release view "$release_tag" \
    --repo "$REPO_FULL_NAME" \
    --json isDraft,targetCommitish 2>"$release_error_file"); then
    release_error=$(cat "$release_error_file")
    rm -f "$release_error_file"
    case "$release_error" in
      *"release not found"*|*"HTTP 404"*|*"Not Found"*)
        return 1
        ;;
    esac

    printf '%s\n' "$release_error" >&2
    return 2
  fi

  rm -f "$release_error_file"
  release_is_draft=$(printf '%s\n' "$release_json" | jq -r '.isDraft')
  release_target_sha=$(printf '%s\n' "$release_json" | jq -r '.targetCommitish')

  [ "$release_is_draft" = "false" ] && [ "$release_target_sha" = "$expected_sha" ]
}

PENDING_RELEASE_PRS=$(gh pr list \
  --repo "$REPO_FULL_NAME" \
  --state merged \
  --base "$TARGET_BRANCH" \
  --label "$PENDING_LABEL" \
  --json number,title,body,mergeCommit)

PENDING_RELEASE_PR_COUNT=$(printf '%s\n' "$PENDING_RELEASE_PRS" | jq 'length')
if [ "$PENDING_RELEASE_PR_COUNT" -eq 0 ]; then
  echo "No pending merged release PR labels found for $TARGET_BRANCH."
  exit 0
fi

printf '%s\n' "$PENDING_RELEASE_PRS" | jq -c '.[]' | while IFS= read -r release_pr_json; do
  release_pr_number=$(printf '%s\n' "$release_pr_json" | jq -r '.number')
  release_pr_title=$(printf '%s\n' "$release_pr_json" | jq -r '.title')
  release_pr_body=$(printf '%s\n' "$release_pr_json" | jq -r '.body // ""')
  release_pr_sha=$(printf '%s\n' "$release_pr_json" | jq -r '.mergeCommit.oid')
  release_version=$(release_version_from_pr "$release_pr_title" "$release_pr_body")

  if [ -z "$release_version" ]; then
    echo "Skipping pending PR #$release_pr_number because the title is not a release-please release title: $release_pr_title"
    continue
  fi

  release_tag="v$release_version"
  set +e
  release_is_published_at_sha "$release_tag" "$release_pr_sha"
  release_status=$?
  set -e

  case "$release_status" in
    0)
      gh pr edit "$release_pr_number" \
        --repo "$REPO_FULL_NAME" \
        --remove-label "$PENDING_LABEL" \
        --add-label "$TAGGED_LABEL"
      echo "Marked release PR #$release_pr_number as tagged for $release_tag."
      ;;
    1)
      echo "Pending release PR #$release_pr_number does not have a matching published release yet: $release_tag"
      continue
      ;;
    *)
      exit "$release_status"
      ;;
  esac
done
