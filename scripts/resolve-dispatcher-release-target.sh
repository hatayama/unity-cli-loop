#!/bin/sh
set -eu

ROOT_DIR=$(CDPATH= cd "$(dirname "$0")/.." && pwd)

: "${EVENT_NAME:?EVENT_NAME is required}"

EVENT_REF_NAME=${EVENT_REF_NAME:-}
INPUT_DRY_RUN=${INPUT_DRY_RUN:-false}
RELEASE_DATA=""

release_json() {
  release_tag=$1
  release_error_file=$(mktemp)
  if gh release view "$release_tag" --json isDraft,assets 2>"$release_error_file"; then
    rm -f "$release_error_file"
    return 0
  fi

  release_error=$(cat "$release_error_file")
  rm -f "$release_error_file"

  case "$release_error" in
    *"release not found"*|*"HTTP 404"*|*"Not Found"*)
      return 1
      ;;
  esac

  printf '%s\n' "$release_error" >&2
  return 2
}

release_json_or_exit() {
  release_tag=$1

  set +e
  RELEASE_DATA=$(release_json "$release_tag")
  release_status=$?
  set -e

  case "$release_status" in
    0)
      return 0
      ;;
    1)
      return 1
      ;;
    *)
      exit "$release_status"
      ;;
  esac
}

release_has_all_dispatcher_assets() {
  release_tag=$1
  release_json_or_exit "$release_tag" || return 1
  release_data=$RELEASE_DATA
  if [ -z "$release_data" ]; then
    return 1
  fi

  for asset_name in $("$ROOT_DIR/scripts/verify-dispatcher-release-assets.sh" --list); do
    asset_count=$(printf '%s\n' "$release_data" | jq --arg name "$asset_name" '[.assets[]? | select(.name == $name and .size > 0)] | length')
    if [ "$asset_count" -eq 0 ]; then
      return 1
    fi
  done

  return 0
}

release_is_published_with_dispatcher_assets() {
  release_tag=$1
  release_json_or_exit "$release_tag" || return 1
  release_data=$RELEASE_DATA
  if [ -z "$release_data" ]; then
    return 1
  fi

  is_draft=$(printf '%s\n' "$release_data" | jq -r '.isDraft')
  if [ "$is_draft" != "false" ]; then
    return 1
  fi

  release_has_all_dispatcher_assets "$release_tag"
}

release_is_published() {
  release_tag=$1
  release_json_or_exit "$release_tag" || return 1
  release_data=$RELEASE_DATA
  if [ -z "$release_data" ]; then
    return 1
  fi

  is_draft=$(printf '%s\n' "$release_data" | jq -r '.isDraft')
  [ "$is_draft" = "false" ]
}

VERSION=$(jq -r '.dispatcherVersion' cli/dispatcher-contract.json)
if [ -z "$VERSION" ] || [ "$VERSION" = "null" ]; then
  echo "Could not resolve dispatcherVersion from cli/dispatcher-contract.json." >&2
  exit 1
fi

RELEASE_TAG="dispatcher-v$VERSION"
case "$RELEASE_TAG" in
  dispatcher-v[0-9]*)
    ;;
  *)
    echo "Invalid dispatcher release tag: $RELEASE_TAG" >&2
    exit 1
    ;;
esac

case "$RELEASE_TAG" in
  *[!A-Za-z0-9._-]*)
    echo "Invalid dispatcher release tag: $RELEASE_TAG" >&2
    exit 1
    ;;
esac

SHOULD_PUBLISH=false
SHOULD_RELEASE=false
CAN_EVALUATE_DISPATCHER_RELEASE=true
if [ "$EVENT_NAME" = "push" ]; then
  case "$EVENT_REF_NAME" in
    main|v3-beta)
      ;;
    *)
      echo "Skipping dispatcher publish for unsupported branch $EVENT_REF_NAME." >&2
      CAN_EVALUATE_DISPATCHER_RELEASE=false
      ;;
  esac
fi

TARGET_SHA=$(git rev-parse HEAD)

if [ "$CAN_EVALUATE_DISPATCHER_RELEASE" != "true" ]; then
  SHOULD_PUBLISH=false
  SHOULD_RELEASE=false
elif release_is_published_with_dispatcher_assets "$RELEASE_TAG"; then
  SHOULD_PUBLISH=false
  SHOULD_RELEASE=false
else
  SHOULD_PUBLISH=true
  if release_is_published "$RELEASE_TAG"; then
    SHOULD_RELEASE=false
  else
    SHOULD_RELEASE=true
  fi
fi

DRY_RUN=false
if [ "$INPUT_DRY_RUN" = "true" ]; then
  DRY_RUN=true
fi

printf 'publish=%s\n' "$SHOULD_PUBLISH"
printf 'release=%s\n' "$SHOULD_RELEASE"
printf 'tag=%s\n' "$RELEASE_TAG"
printf 'version=%s\n' "$VERSION"
printf 'sha=%s\n' "$TARGET_SHA"
printf 'dry_run=%s\n' "$DRY_RUN"

echo "Dispatcher publish: $SHOULD_PUBLISH" >&2
echo "Dispatcher release tag: $RELEASE_TAG" >&2
echo "Target SHA: $TARGET_SHA" >&2
