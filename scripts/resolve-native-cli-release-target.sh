#!/bin/sh
set -eu

ROOT_DIR=$(CDPATH= cd "$(dirname "$0")/.." && pwd)

: "${EVENT_NAME:?EVENT_NAME is required}"

EVENT_REF_NAME=${EVENT_REF_NAME:-}
INPUT_RELEASE_TAG=${INPUT_RELEASE_TAG:-}
INPUT_DRY_RUN=${INPUT_DRY_RUN:-false}
RELEASE_DATA=""
CLI_RELEASE_INPUT_PATHS="
cli/.go-version
cli/layout-contract.json
cli/contract.go
cli/contract.json
cli/contract_test.go
cli/cmd
cli/go.mod
cli/go.sum
cli/internal
scripts/build-go-cli.sh
scripts/go-cli-toolchain.sh
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

latest_cli_asset_release_tag() {
  excluded_tag=$1
  release_list=$(gh release list --limit 100 --json tagName,isDraft)
  release_tags=$(printf '%s\n' "$release_list" | jq -r '.[] | select(.isDraft == false) | .tagName')
  for release_tag in $release_tags; do
    if [ "$release_tag" = "$excluded_tag" ]; then
      continue
    fi

    if release_has_all_cli_assets "$release_tag"; then
      printf '%s\n' "$release_tag"
      return
    fi
  done
}

cli_release_inputs_changed() {
  base_ref=$1
  head_ref=$2

  if ! git diff --quiet "$base_ref" "$head_ref" -- $CLI_RELEASE_INPUT_PATHS; then
    return 0
  fi

  return 1
}

release_commit_updates_cli_version() {
  commit_sha=$1
  version=$2
  expected_manifest_entry="\"cli\": \"$version\""
  expected_changelog_heading="## [$version]"

  commit_diff=$(git show --format= "$commit_sha" -- .release-please-manifest.json cli/CHANGELOG.md 2>/dev/null || true)
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

release_commit_sha_for_version() {
  version=$1
  build_sha=$2
  release_branch=${EVENT_REF_NAME:-}

  git log --format='%H	%s' "$build_sha" \
    | awk -F '	' -v version="$version" -v release_branch="$release_branch" '
      function value_appears_as_release_token(remainder, value, parts, part_index) {
        split(remainder, parts, " ")
        for (part_index in parts) {
          if (parts[part_index] == value) {
            return 1
          }
        }
        return 0
      }

      function release_remainder_matches_version(remainder) {
        if (value_appears_as_release_token(remainder, version)) {
          return "version"
        }

        return ""
      }

      function release_remainder_matches_branch(remainder) {
        if (release_branch != "" && value_appears_as_release_token(remainder, release_branch)) {
          return "branch"
        }

        return ""
      }

      function is_release_please_subject(subject) {
        plain_prefix = "chore: release "
        scoped_marker = "): release "

        if (index(subject, plain_prefix) == 1) {
          release_remainder = substr(subject, length(plain_prefix) + 1)
          release_match = release_remainder_matches_version(release_remainder)
          if (release_match != "") {
            return release_match
          }

          return release_remainder_matches_branch(release_remainder)
        }

        if (index(subject, "chore(") != 1) {
          return ""
        }

        scope_end = index(subject, ")")
        marker_start = scope_end
        marker = substr(subject, marker_start, length(scoped_marker))
        if (scope_end == 0 || marker != scoped_marker) {
          return ""
        }

        release_remainder = substr(subject, marker_start + length(scoped_marker))
        release_match = release_remainder_matches_version(release_remainder)
        if (release_match != "") {
          return release_match
        }

        return release_remainder_matches_branch(release_remainder)
      }

      {
        release_match = is_release_please_subject($2)
        if (release_match != "") {
          print $1 "\t" release_match
        }
      }
    ' \
    | while IFS='	' read -r candidate_sha release_match; do
      if [ "$release_match" = "version" ]; then
        printf '%s\n' "$candidate_sha"
        return
      fi

      if release_commit_updates_cli_version "$candidate_sha" "$version"; then
        printf '%s\n' "$candidate_sha"
        return
      fi
    done
}

VERSION=$(jq -r '.["cli"]' .release-please-manifest.json)
if [ -z "$VERSION" ] || [ "$VERSION" = "null" ]; then
  echo "Could not resolve project runner release version from .release-please-manifest.json." >&2
  exit 1
fi

RELEASE_TAG="${INPUT_RELEASE_TAG:-uloop-project-runner-v$VERSION}"
case "$RELEASE_TAG" in
  uloop-project-runner-v[0-9]*)
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
RELEASE_TARGET_SHA=$(release_commit_sha_for_version "$VERSION" "$BUILD_SHA")
if [ -z "$RELEASE_TARGET_SHA" ]; then
  RELEASE_TARGET_SHA=$BUILD_SHA
fi

if [ "$CAN_EVALUATE_CLI_RELEASE" != "true" ]; then
  SHOULD_PUBLISH=false
  SHOULD_RELEASE=false
elif release_is_published "$RELEASE_TAG"; then
  SHOULD_RELEASE=false
else
  SHOULD_RELEASE=true
fi

if [ "$CAN_EVALUATE_CLI_RELEASE" != "true" ]; then
  SHOULD_PUBLISH=false
elif release_is_published_with_cli_assets "$RELEASE_TAG"; then
  SHOULD_PUBLISH=false
else
  PREVIOUS_CLI_RELEASE_TAG=$(latest_cli_asset_release_tag "$RELEASE_TAG")
  if [ -z "$PREVIOUS_CLI_RELEASE_TAG" ]; then
    echo "No previous project runner asset release found; publishing native project runner assets." >&2
    SHOULD_PUBLISH=true
  elif release_commit_updates_cli_version "$RELEASE_TARGET_SHA" "$VERSION"; then
    echo "Project runner release metadata changed in $RELEASE_TARGET_SHA; publishing native project runner assets." >&2
    SHOULD_PUBLISH=true
  elif cli_release_inputs_changed "$PREVIOUS_CLI_RELEASE_TAG" "$TARGET_SHA"; then
    echo "Project runner release inputs changed since $PREVIOUS_CLI_RELEASE_TAG; publishing native project runner assets." >&2
    SHOULD_PUBLISH=true
  else
    echo "Project runner release inputs are unchanged since $PREVIOUS_CLI_RELEASE_TAG; skipping native CLI publish." >&2
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
