#!/bin/sh
set -eu

ROOT_DIR=$(CDPATH= cd "$(dirname "$0")/.." && pwd)

: "${EVENT_NAME:?EVENT_NAME is required}"

EVENT_REF_NAME=${EVENT_REF_NAME:-}
INPUT_RELEASE_TAG=${INPUT_RELEASE_TAG:-}
INPUT_DRY_RUN=${INPUT_DRY_RUN:-false}
DISPATCHER_CONTRACT_PATH="Packages/src/Cli~/Dispatcher~/contract.json"
CORE_CONTRACT_PATH="Packages/src/Cli~/Core~/contract.json"
RELEASE_DATA=""
DISPATCHER_RELEASE_INPUT_PATHS="
Packages/src/Cli~/.go-version
Packages/src/Cli~/layout-contract.json
Packages/src/Cli~/Dispatcher~/cmd
Packages/src/Cli~/Dispatcher~/internal
Packages/src/Cli~/Dispatcher~/contract.go
Packages/src/Cli~/Dispatcher~/contract_test.go
Packages/src/Cli~/Dispatcher~/go.mod
Packages/src/Cli~/Dispatcher~/go.sum
Packages/src/Cli~/Shared~
scripts/build-go-cli.sh
scripts/go-cli-toolchain.sh
scripts/install.sh
scripts/install.ps1
scripts/package-go-cli.sh
scripts/verify-native-cli-release-assets.sh
"

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

  for asset_name in $("$ROOT_DIR/scripts/verify-native-cli-release-assets.sh" --list); do
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

latest_dispatcher_asset_release_tag() {
  excluded_tag=$1
  release_list=$(gh release list --limit 100 --json tagName,isDraft)
  release_tags=$(printf '%s\n' "$release_list" | jq -r '.[] | select(.isDraft == false) | .tagName')
  for release_tag in $release_tags; do
    if [ "$release_tag" = "$excluded_tag" ]; then
      continue
    fi

    if release_has_all_dispatcher_assets "$release_tag"; then
      printf '%s\n' "$release_tag"
      return
    fi
  done
}

normalized_dispatcher_contract() {
  git_ref=$1
  git show "$git_ref:$DISPATCHER_CONTRACT_PATH" | jq 'del(.dispatcherVersion)'
}

dispatcher_contract_changed_except_version() {
  base_ref=$1
  head_ref=$2
  base_contract=$(normalized_dispatcher_contract "$base_ref") || return 0
  head_contract=$(normalized_dispatcher_contract "$head_ref") || return 0

  [ "$base_contract" != "$head_contract" ]
}

core_required_dispatcher_version() {
  git_ref=$1
  git show "$git_ref:$CORE_CONTRACT_PATH" | jq -r '.minimumRequiredDispatcherVersion'
}

core_required_dispatcher_version_changed() {
  base_ref=$1
  head_ref=$2
  base_required_version=$(core_required_dispatcher_version "$base_ref") || return 0
  head_required_version=$(core_required_dispatcher_version "$head_ref") || return 0

  [ "$base_required_version" != "$head_required_version" ]
}

dispatcher_release_inputs_changed() {
  base_ref=$1
  head_ref=$2

  if ! git diff --quiet "$base_ref" "$head_ref" -- $DISPATCHER_RELEASE_INPUT_PATHS; then
    return 0
  fi

  dispatcher_contract_changed_except_version "$base_ref" "$head_ref" \
    || core_required_dispatcher_version_changed "$base_ref" "$head_ref"
}

release_commit_sha_for_version() {
  version=$1
  build_sha=$2

  git log --format='%H	%s' "$build_sha" \
    | awk -F '	' -v version="$version" '
      {
        marker = "release " version
        marker_index = index($2, marker)
        next_character = substr($2, marker_index + length(marker), 1)
      }
      marker_index > 0 && (next_character == "" || next_character == " ") {
        print $1
        exit
      }
    '
}

VERSION=$(jq -r '.["Packages/src"]' .release-please-manifest.json)
if [ -z "$VERSION" ] || [ "$VERSION" = "null" ]; then
  echo "Could not resolve Packages/src version from .release-please-manifest.json." >&2
  exit 1
fi

RELEASE_TAG="${INPUT_RELEASE_TAG:-v$VERSION}"
case "$RELEASE_TAG" in
  v[0-9]*)
    ;;
  *)
    echo "Invalid release tag: $RELEASE_TAG" >&2
    exit 1
    ;;
esac

case "$RELEASE_TAG" in
  *[!A-Za-z0-9._-]*)
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
CAN_EVALUATE_DISPATCHER_RELEASE=true
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
      CAN_EVALUATE_DISPATCHER_RELEASE=false
      ;;
  esac
fi

TARGET_SHA=$(git rev-parse HEAD)
BUILD_SHA=$TARGET_SHA
RELEASE_TARGET_SHA=$(release_commit_sha_for_version "$VERSION" "$BUILD_SHA")
if [ -z "$RELEASE_TARGET_SHA" ]; then
  RELEASE_TARGET_SHA=$BUILD_SHA
fi

if [ "$CAN_EVALUATE_DISPATCHER_RELEASE" != "true" ]; then
  SHOULD_PUBLISH=false
  SHOULD_RELEASE=false
elif release_is_published "$RELEASE_TAG"; then
  SHOULD_RELEASE=false
else
  SHOULD_RELEASE=true
fi

if [ "$CAN_EVALUATE_DISPATCHER_RELEASE" != "true" ]; then
  SHOULD_PUBLISH=false
elif release_is_published_with_dispatcher_assets "$RELEASE_TAG"; then
  SHOULD_PUBLISH=false
else
  PREVIOUS_DISPATCHER_RELEASE_TAG=$(latest_dispatcher_asset_release_tag "$RELEASE_TAG")
  if [ -z "$PREVIOUS_DISPATCHER_RELEASE_TAG" ]; then
    echo "No previous Dispatcher asset release found; publishing native CLI assets." >&2
    SHOULD_PUBLISH=true
  elif dispatcher_release_inputs_changed "$PREVIOUS_DISPATCHER_RELEASE_TAG" "$TARGET_SHA"; then
    echo "Dispatcher release inputs changed since $PREVIOUS_DISPATCHER_RELEASE_TAG; publishing native CLI assets." >&2
    SHOULD_PUBLISH=true
  else
    echo "Dispatcher release inputs are unchanged since $PREVIOUS_DISPATCHER_RELEASE_TAG; skipping native CLI publish." >&2
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
