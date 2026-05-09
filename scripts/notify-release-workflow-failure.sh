#!/bin/sh
set -eu

: "${WORKFLOW_NAME:?WORKFLOW_NAME is required}"
: "${HEAD_BRANCH:?HEAD_BRANCH is required}"
: "${HEAD_SHA:?HEAD_SHA is required}"
: "${RUN_ID:?RUN_ID is required}"
: "${RUN_URL:?RUN_URL is required}"
: "${CONCLUSION:?CONCLUSION is required}"

LABEL_NAME=release-failure
TITLE="Release workflow failed: $WORKFLOW_NAME on $HEAD_BRANCH"
BODY_FILE=$(mktemp)

cleanup() {
  rm -f "$BODY_FILE"
}

trap cleanup EXIT INT HUP TERM

cat > "$BODY_FILE" <<EOF_BODY
The release workflow failed and needs attention.

- Workflow: $WORKFLOW_NAME
- Branch: $HEAD_BRANCH
- Commit: $HEAD_SHA
- Conclusion: $CONCLUSION
- Run ID: $RUN_ID
- Run URL: $RUN_URL
EOF_BODY

gh label create "$LABEL_NAME" \
  --description "Release workflow failures that need attention" \
  --color B60205 \
  --force >/dev/null

ISSUES_JSON=$(gh issue list \
  --state open \
  --label "$LABEL_NAME" \
  --limit 1000 \
  --json number,title) || {
    echo "Could not list existing release failure issues." >&2
    exit 1
  }
EXISTING_ISSUE_NUMBER=$(printf '%s\n' "$ISSUES_JSON" | jq -r --arg title "$TITLE" 'map(select(.title == $title)) | .[0].number // empty')

if [ -n "$EXISTING_ISSUE_NUMBER" ]; then
  gh issue comment "$EXISTING_ISSUE_NUMBER" --body-file "$BODY_FILE" >/dev/null
  echo "Updated release failure issue #$EXISTING_ISSUE_NUMBER."
  exit 0
fi

gh issue create \
  --title "$TITLE" \
  --body-file "$BODY_FILE" \
  --label "$LABEL_NAME" >/dev/null

echo "Created release failure issue: $TITLE"
