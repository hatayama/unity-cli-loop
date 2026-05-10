#!/bin/sh
set -eu

: "${RELEASE_TAG:?RELEASE_TAG is required}"
: "${TARGET_SHA:?TARGET_SHA is required}"
: "${TARGET_BRANCH:?TARGET_BRANCH is required}"

REPO_FULL_NAME=${GITHUB_REPOSITORY:-hatayama/unity-cli-loop}
PENDING_LABEL="autorelease: pending"
TAGGED_LABEL="autorelease: tagged"

RELEASE_JSON=$(gh release view "$RELEASE_TAG" \
  --repo "$REPO_FULL_NAME" \
  --json isDraft,targetCommitish)
RELEASE_IS_DRAFT=$(printf '%s\n' "$RELEASE_JSON" | jq -r '.isDraft')
RELEASE_TARGET_SHA=$(printf '%s\n' "$RELEASE_JSON" | jq -r '.targetCommitish')

if [ "$RELEASE_IS_DRAFT" != "false" ]; then
  echo "Release $RELEASE_TAG is still draft; leaving release PR labels unchanged."
  exit 0
fi

if [ "$RELEASE_TARGET_SHA" != "$TARGET_SHA" ]; then
  echo "Release $RELEASE_TAG points at $RELEASE_TARGET_SHA, expected $TARGET_SHA." >&2
  exit 1
fi

PENDING_RELEASE_PRS=$(gh pr list \
  --repo "$REPO_FULL_NAME" \
  --state merged \
  --base "$TARGET_BRANCH" \
  --label "$PENDING_LABEL" \
  --json number,title,mergeCommit)
MATCHING_RELEASE_PRS=$(printf '%s\n' "$PENDING_RELEASE_PRS" \
  | jq --arg target_sha "$TARGET_SHA" '[.[] | select(.mergeCommit.oid == $target_sha)]')
MATCHING_RELEASE_PR_COUNT=$(printf '%s\n' "$MATCHING_RELEASE_PRS" | jq 'length')

case "$MATCHING_RELEASE_PR_COUNT" in
  0)
    echo "No pending release PR found for $RELEASE_TAG at $TARGET_SHA."
    exit 0
    ;;
  1)
    ;;
  *)
    echo "Expected one pending release PR for $RELEASE_TAG at $TARGET_SHA, found $MATCHING_RELEASE_PR_COUNT." >&2
    exit 1
    ;;
esac

RELEASE_PR_NUMBER=$(printf '%s\n' "$MATCHING_RELEASE_PRS" | jq -r '.[0].number')
gh pr edit "$RELEASE_PR_NUMBER" \
  --repo "$REPO_FULL_NAME" \
  --remove-label "$PENDING_LABEL" \
  --add-label "$TAGGED_LABEL"

echo "Marked release PR #$RELEASE_PR_NUMBER as tagged for $RELEASE_TAG."
