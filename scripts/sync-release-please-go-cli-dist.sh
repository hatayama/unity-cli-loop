#!/bin/sh
set -eu

: "${TARGET_BRANCH:?TARGET_BRANCH is required}"

REPO_FULL_NAME=${GITHUB_REPOSITORY:-hatayama/unity-cli-loop}
RELEASE_PR_BRANCH="release-please--branches--$TARGET_BRANCH"
DIST_PATHS="
Packages/src/Cli~/dist
"

RELEASE_PRS=$(gh pr list \
  --repo "$REPO_FULL_NAME" \
  --state open \
  --base "$TARGET_BRANCH" \
  --label "autorelease: pending" \
  --json number,headRefName,title,url)

MATCHING_RELEASE_PRS=$(printf '%s' "$RELEASE_PRS" | jq --arg release_branch "$RELEASE_PR_BRANCH" '
  def is_release_please_title:
    (. // "") | test("^chore(\\([^)]*\\))?: release( |$)");

  [
    .[]
    | select(.title | is_release_please_title)
    | select(
        (.headRefName // "") == $release_branch
        or ((.headRefName // "") | startswith($release_branch + "--components--"))
      )
  ]
')
MATCHING_RELEASE_PR_COUNT=$(printf '%s' "$MATCHING_RELEASE_PRS" | jq 'length')

case "$MATCHING_RELEASE_PR_COUNT" in
  0)
    echo "No pending release-please PR found for $TARGET_BRANCH."
    exit 0
    ;;
  1)
    ;;
  *)
    echo "Expected one pending release-please PR for $TARGET_BRANCH, found $MATCHING_RELEASE_PR_COUNT." >&2
    exit 1
    ;;
esac

RELEASE_PR_NUMBER=$(printf '%s' "$MATCHING_RELEASE_PRS" | jq -r '.[0].number')
RELEASE_PR_HEAD_REF=$(printf '%s' "$MATCHING_RELEASE_PRS" | jq -r '.[0].headRefName')
RELEASE_PR_URL=$(printf '%s' "$MATCHING_RELEASE_PRS" | jq -r '.[0].url')

echo "Syncing native CLI dist files for release PR #$RELEASE_PR_NUMBER: $RELEASE_PR_URL"
echo "Marking release PR #$RELEASE_PR_NUMBER as draft while generated files are synced."
gh pr ready "$RELEASE_PR_NUMBER" --repo "$REPO_FULL_NAME" --undo

git fetch origin "$RELEASE_PR_HEAD_REF"
git checkout -B "$RELEASE_PR_HEAD_REF" FETCH_HEAD

scripts/build-go-cli.sh

UNTRACKED_DIST_FILES=$(git ls-files --others --exclude-standard -- $DIST_PATHS)
if git diff --quiet -- $DIST_PATHS && [ -z "$UNTRACKED_DIST_FILES" ]; then
  echo "Native CLI dist files are already current."
  scripts/check-go-cli.sh
  exit 0
fi

git config user.name "github-actions[bot]"
git config user.email "41898282+github-actions[bot]@users.noreply.github.com"
git add $DIST_PATHS
git commit -m "chore($TARGET_BRANCH): update native CLI binaries"

scripts/check-go-cli.sh
git push origin "HEAD:$RELEASE_PR_HEAD_REF"
