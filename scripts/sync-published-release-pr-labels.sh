#!/bin/sh
set -eu

: "${TARGET_BRANCH:?TARGET_BRANCH is required}"

REPO_FULL_NAME=${GITHUB_REPOSITORY:-hatayama/unity-cli-loop}
PENDING_LABEL="autorelease: pending"
TAGGED_LABEL="autorelease: tagged"

release_tag_from_title() {
  title=$1

  printf '%s\n' "$title" | jq -R -r '
    try (capture("^chore(\\([^)]*\\))?: release (?<version>[0-9][A-Za-z0-9._-]*)$") | "v" + .version) catch ""
  '
}

release_tag_from_body() {
  body=$1

  printf '%s\n' "$body" | jq -R -s -r '
    def release_tag($component; $version):
      if $component == "" or $component == "unity-package" then
        "v" + $version
      elif $component == "uloop-project-runner" then
        "uloop-project-runner-v" + $version
      elif $component == "dispatcher" or $component == "uloop-dispatcher" then
        "dispatcher-v" + $version
      else
        ""
      end;

    try (
      capture("<summary>(?:(?<component>[^<:]+):\\s*)?(?<version>[0-9][A-Za-z0-9._-]*)</summary>") |
      release_tag((.component // ""); .version)
    ) catch ""
  '
}

# The head branch is the only component marker release-please always writes, so
# it outranks the summary label and the title once release PRs are split per
# component: a component PR body can carry a bare "<version>" summary that the
# body resolver would read as the Unity package.
release_component_from_head_ref() {
  head_ref=$1

  case "$head_ref" in
    *--components--*)
      printf '%s\n' "${head_ref##*--components--}"
      ;;
  esac
}

release_version_from_body() {
  body=$1

  printf '%s\n' "$body" | jq -R -s -r '
    try (
      capture("<summary>(?:[^<:]+:\\s*)?(?<version>[0-9][A-Za-z0-9._-]*)</summary>") | .version
    ) catch ""
  '
}

release_version_from_title() {
  title=$1

  printf '%s\n' "$title" | jq -R -r '
    try (
      capture("^chore(\\([^)]*\\))?: release (?:[^ ]+ )?(?<version>[0-9][A-Za-z0-9._-]*)$") | .version
    ) catch ""
  '
}

release_tag_for_component() {
  component=$1
  version=$2

  jq -n -r --arg component "$component" --arg version "$version" '
    if $version == "" then
      ""
    elif $component == "" or $component == "unity-package" then
      "v" + $version
    elif $component == "uloop-project-runner" then
      "uloop-project-runner-v" + $version
    elif $component == "dispatcher" or $component == "uloop-dispatcher" then
      "dispatcher-v" + $version
    else
      ""
    end
  '
}

release_tag_from_pr() {
  title=$1
  body=$2
  head_ref=$3

  release_component=$(release_component_from_head_ref "$head_ref")
  if [ -n "$release_component" ]; then
    release_version=$(release_version_from_body "$body")
    if [ -z "$release_version" ]; then
      release_version=$(release_version_from_title "$title")
    fi

    release_tag=$(release_tag_for_component "$release_component" "$release_version")
    if [ -n "$release_tag" ]; then
      printf '%s\n' "$release_tag"
      return
    fi
  fi

  release_tag=$(release_tag_from_body "$body")

  if [ -n "$release_tag" ]; then
    printf '%s\n' "$release_tag"
    return
  fi

  release_tag_from_title "$title"
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
  --json number,title,body,headRefName,mergeCommit)

PENDING_RELEASE_PR_COUNT=$(printf '%s\n' "$PENDING_RELEASE_PRS" | jq 'length')
if [ "$PENDING_RELEASE_PR_COUNT" -eq 0 ]; then
  echo "No pending merged release PR labels found for $TARGET_BRANCH."
  exit 0
fi

printf '%s\n' "$PENDING_RELEASE_PRS" | jq -c '.[]' | while IFS= read -r release_pr_json; do
  release_pr_number=$(printf '%s\n' "$release_pr_json" | jq -r '.number')
  release_pr_title=$(printf '%s\n' "$release_pr_json" | jq -r '.title')
  release_pr_body=$(printf '%s\n' "$release_pr_json" | jq -r '.body // ""')
  release_pr_head_ref=$(printf '%s\n' "$release_pr_json" | jq -r '.headRefName // ""')
  release_pr_sha=$(printf '%s\n' "$release_pr_json" | jq -r '.mergeCommit.oid')
  release_tag=$(release_tag_from_pr "$release_pr_title" "$release_pr_body" "$release_pr_head_ref")

  if [ -z "$release_tag" ]; then
    echo "Skipping pending PR #$release_pr_number because the title is not a release-please release title: $release_pr_title"
    continue
  fi

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
