#!/bin/sh
set -eu

ROOT_DIR=$(CDPATH= cd "$(dirname "$0")/.." && pwd)

: "${EVENT_NAME:?EVENT_NAME is required}"

EVENT_REF_NAME=${EVENT_REF_NAME:-}
INPUT_RELEASE_TAG=${INPUT_RELEASE_TAG:-}
INPUT_DRY_RUN=${INPUT_DRY_RUN:-false}
RELEASE_DATA=""

is_semver_version() {
  version=$1
  printf '%s\n' "$version" | grep -Eq '^(0|[1-9][0-9]*)[.](0|[1-9][0-9]*)[.](0|[1-9][0-9]*)(-(0|[1-9][0-9]*|[0-9A-Za-z-]*[A-Za-z-][0-9A-Za-z-]*)([.](0|[1-9][0-9]*|[0-9A-Za-z-]*[A-Za-z-][0-9A-Za-z-]*))*)?([+][0-9A-Za-z-]+([.][0-9A-Za-z-]+)*)?$'
}

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

release_has_all_cli_assets() {
  release_tag=$1
  release_json_or_exit "$release_tag" || return 1
  release_data=$RELEASE_DATA
  if [ -z "$release_data" ]; then
    return 1
  fi

  for asset_name in $("$ROOT_DIR/scripts/verify-native-cli-release-assets.sh" --list); do
    asset_count=$(printf '%s\n' "$release_data" | jq --arg name "$asset_name" '[.assets[]? | select(.name == $name and .size > 0)] | length')
    if [ "$asset_count" -eq 0 ]; then
      return 1
    fi
  done

  return 0
}

release_is_published_with_cli_assets() {
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

  release_has_all_cli_assets "$release_tag"
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

release_commit_updates_cli_version() {
  commit_sha=$1
  version=$2
  expected_manifest_entry="\"cli/project-runner\": \"$version\""
  expected_changelog_heading="## [$version]"

  commit_diff=$(git show --format= "$commit_sha" -- .release-please-manifest.json cli/project-runner/CHANGELOG.md 2>/dev/null || true)
  printf '%s\n' "$commit_diff" \
    | awk -v manifest_entry="$expected_manifest_entry" -v changelog_heading="$expected_changelog_heading" '
      substr($0, 1, 1) == "+" && (index($0, manifest_entry) > 0 || index($0, changelog_heading) > 0) {
        found = 1
      }
      END {
        exit found ? 0 : 1
      }
    '
}

VERSION=$(jq -r '.["cli/project-runner"]' .release-please-manifest.json)
if [ -z "$VERSION" ] || [ "$VERSION" = "null" ]; then
  echo "Could not resolve project runner release version from .release-please-manifest.json." >&2
  exit 1
fi

RELEASE_TAG="${INPUT_RELEASE_TAG:-uloop-project-runner-v$VERSION}"
case "$RELEASE_TAG" in
  uloop-project-runner-v*)
    ;;
  *)
    echo "Invalid release tag: $RELEASE_TAG" >&2
    exit 1
    ;;
esac

RELEASE_TAG_VERSION=${RELEASE_TAG#uloop-project-runner-v}
if ! is_semver_version "$RELEASE_TAG_VERSION"; then
  echo "Invalid release tag: $RELEASE_TAG" >&2
  exit 1
fi

case "$RELEASE_TAG" in
  *[!A-Za-z0-9._+-]*)
    echo "Invalid release tag: $RELEASE_TAG" >&2
    exit 1
    ;;
esac

IS_PRERELEASE=false
case "$VERSION" in
  *-*)
    IS_PRERELEASE=true
    ;;
esac

SHOULD_PUBLISH=false
SHOULD_RELEASE=false
CAN_EVALUATE_CLI_RELEASE=true
if [ "$EVENT_NAME" = "push" ]; then
  case "$EVENT_REF_NAME" in
    main)
      if [ "$IS_PRERELEASE" = "true" ]; then
        echo "Refusing to publish prerelease version $VERSION from main." >&2
        exit 1
      fi
      ;;
    v3-beta)
      if [ "$IS_PRERELEASE" != "true" ]; then
        echo "Refusing to publish stable version $VERSION from v3-beta." >&2
        exit 1
      fi
      ;;
    *)
      echo "Skipping native CLI publish for unsupported branch $EVENT_REF_NAME." >&2
      CAN_EVALUATE_CLI_RELEASE=false
      ;;
  esac
fi

TARGET_SHA=$(git rev-parse HEAD)
BUILD_SHA=$TARGET_SHA
RELEASE_TARGET_SHA=$BUILD_SHA

if [ "$CAN_EVALUATE_CLI_RELEASE" != "true" ]; then
  SHOULD_PUBLISH=false
  SHOULD_RELEASE=false
elif [ "$EVENT_NAME" = "push" ] && ! release_commit_updates_cli_version "$BUILD_SHA" "$VERSION"; then
  echo "HEAD commit $BUILD_SHA does not stamp project runner version $VERSION; skipping native CLI publish. Retry an incomplete release with workflow_dispatch." >&2
  SHOULD_PUBLISH=false
  SHOULD_RELEASE=false
elif release_is_published_with_cli_assets "$RELEASE_TAG"; then
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
printf 'sha=%s\n' "$RELEASE_TARGET_SHA"
printf 'build_sha=%s\n' "$BUILD_SHA"
printf 'dry_run=%s\n' "$DRY_RUN"

echo "Publish: $SHOULD_PUBLISH" >&2
echo "Release tag: $RELEASE_TAG" >&2
echo "Target SHA: $RELEASE_TARGET_SHA" >&2
echo "Build SHA: $BUILD_SHA" >&2
