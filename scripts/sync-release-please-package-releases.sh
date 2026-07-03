#!/bin/sh
set -eu

ROOT_DIR=${ULOOP_REPO_ROOT:-$(CDPATH= cd "$(dirname "$0")/.." && pwd)}
# SCRIPT_DIR must resolve from $0, not ROOT_DIR: ULOOP_REPO_ROOT may point at a
# repository that does not contain the helper scripts this script calls.
SCRIPT_DIR=$(CDPATH= cd "$(dirname "$0")" && pwd)
CONFIG="$ROOT_DIR/release-please-config.json"
MANIFEST="$ROOT_DIR/.release-please-manifest.json"
CLI_PACKAGE_PATH="project-runner"
# dispatcher-publish owns the dispatcher tag/draft/assets/publish flow. Creating
# the release here would publish it before its assets are uploaded, so the sync
# must skip the dispatcher package entirely.
DISPATCHER_PACKAGE_PATH="dispatcher"
UNITY_PACKAGE_CLI_PIN_FILE="Packages/src/project-runner-pin.json"
REPO_FULL_NAME=${GITHUB_REPOSITORY:-hatayama/unity-cli-loop}
TMP_DIR=$(mktemp -d)

cd "$ROOT_DIR"

cleanup() {
  rm -rf "$TMP_DIR"
}

trap cleanup EXIT INT HUP TERM

mark_package_release_sync_ready() {
  ready=$1

  if [ -n "${GITHUB_OUTPUT:-}" ]; then
    printf 'ready=%s\n' "$ready" >> "$GITHUB_OUTPUT"
  fi
}

strip_carriage_returns() {
  tr -d '\015'
}

resolve_package_path() {
  package_path=$1
  file_path=$2

  case "$file_path" in
    /*)
      printf '%s\n' "${file_path#/}"
      ;;
    *)
      printf '%s/%s\n' "$package_path" "$file_path"
      ;;
  esac
}

release_json() {
  release_tag=$1
  release_error_file=$(mktemp)
  if gh release view "$release_tag" --repo "$REPO_FULL_NAME" --json isDraft,targetCommitish,assets 2>"$release_error_file"; then
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

release_commit_sha_for_package() {
  package_path=$1
  version=$2
  changelog_path=$3

  # Only release-please release commits may qualify. Content matching alone is
  # not enough: a commit that moves a package changelog re-adds every changelog
  # line in the pathspec-limited diff (the rename source is outside the
  # pathspec), so it would otherwise impersonate the release commit.
  git log --format='%H%x09%s' HEAD |
  while IFS='	' read -r commit_sha commit_subject; do
    if ! "$SCRIPT_DIR/is-release-please-release-commit.sh" "$commit_subject"; then
      continue
    fi
    if release_commit_updates_package_version "$commit_sha" "$package_path" "$version" "$changelog_path"; then
      printf '%s\n' "$commit_sha"
      break
    fi
  done
}

release_commit_updates_package_version() {
  commit_sha=$1
  package_path=$2
  version=$3
  changelog_path=$4
  expected_manifest_entry="\"$package_path\": \"$version\""
  expected_changelog_heading="## [$version]"

  # Require both the manifest entry and the changelog heading: a commit that only
  # rewrites the manifest line (for example a package key rename at an unchanged
  # version) must not be mistaken for the release-please release commit.
  git show --format= "$commit_sha" -- .release-please-manifest.json "$changelog_path" 2>/dev/null |
    awk -v manifest_entry="$expected_manifest_entry" -v changelog_heading="$expected_changelog_heading" '
      substr($0, 1, 1) == "+" && index($0, manifest_entry) > 0 {
        manifest_found = 1
      }
      substr($0, 1, 1) == "+" && index($0, changelog_heading) > 0 {
        changelog_found = 1
      }
      END {
        exit (manifest_found && changelog_found) ? 0 : 1
      }
    '
}

release_tag_for_package() {
  component=$1
  include_component=$2
  include_v=$3
  version=$4
  release_tag=""

  if [ "$include_component" = "true" ]; then
    if [ -z "$component" ]; then
      echo "release-please package has include-component-in-tag=true without a component." >&2
      exit 1
    fi

    release_tag="$component-"
  fi

  if [ "$include_v" = "true" ]; then
    release_tag="${release_tag}v"
  fi

  printf '%s%s\n' "$release_tag" "$version"
}

fetch_release_refs() {
  if git remote get-url origin >/dev/null 2>&1; then
    git fetch --force --tags origin '+refs/heads/*:refs/remotes/origin/*' >/dev/null
  fi
}

fetch_cli_release_tag() {
  release_tag=$1

  if git remote get-url origin >/dev/null 2>&1; then
    git fetch --force origin "refs/tags/$release_tag:refs/tags/$release_tag" >/dev/null
  fi
}

resolve_release_target_commit() {
  release_target=$1

  if resolved_commit=$(git rev-parse "$release_target^{commit}" 2>/dev/null); then
    printf '%s\n' "$resolved_commit"
    return
  fi

  case "$release_target" in
    refs/heads/*)
      release_branch=${release_target#refs/heads/}
      ;;
    *)
      release_branch=$release_target
      ;;
  esac

  if resolved_commit=$(git rev-parse "refs/remotes/origin/$release_branch^{commit}" 2>/dev/null); then
    printf '%s\n' "$resolved_commit"
    return
  fi

  printf '%s\n' "$release_target"
}

write_release_notes() {
  changelog_path=$1
  version=$2
  notes_file=$3

  awk -v version="$version" '
    $0 ~ "^## \\[" version "\\]" {
      found = 1
      printing = 1
      print
      next
    }
    printing && /^## \[/ {
      exit
    }
    printing {
      print
    }
    END {
      if (!found) {
        exit 1
      }
    }
  ' "$changelog_path" > "$notes_file"
}

ensure_release_points_to_commit() {
  release_tag=$1
  expected_sha=$2
  release_data=$3

  release_target=$(printf '%s\n' "$release_data" | jq -r '.targetCommitish' | strip_carriage_returns)
  resolved_release_target=$(resolve_release_target_commit "$release_target")
  resolved_expected_sha=$(git rev-parse "$expected_sha^{commit}")

  if [ "$resolved_release_target" != "$resolved_expected_sha" ]; then
    echo "Release $release_tag points at $resolved_release_target, expected $resolved_expected_sha." >&2
    exit 1
  fi
}

publish_existing_draft_release() {
  release_tag=$1
  version=$2
  prerelease_flag=""

  case "$version" in
    *-*)
      prerelease_flag="--prerelease"
      ;;
  esac

  gh release edit "$release_tag" --repo "$REPO_FULL_NAME" --draft=false $prerelease_flag
  echo "Published existing draft release $release_tag."
}

create_package_release() {
  release_tag=$1
  version=$2
  target_sha=$3
  notes_file=$4
  prerelease_flag=""

  case "$version" in
    *-*)
      prerelease_flag="--prerelease"
      ;;
  esac

  existing_tag_sha=$(git rev-list -n 1 "$release_tag" 2>/dev/null || true)
  if [ -n "$existing_tag_sha" ] && [ "$existing_tag_sha" != "$target_sha" ]; then
    echo "Tag $release_tag points at $existing_tag_sha, expected $target_sha." >&2
    exit 1
  fi

  create_error_file="$TMP_DIR/create-release-error.txt"
  set +e
  gh release create "$release_tag" \
    --repo "$REPO_FULL_NAME" \
    --title "$release_tag" \
    --notes-file "$notes_file" \
    --target "$target_sha" \
    $prerelease_flag 2>"$create_error_file"

  release_created=$?
  set -e
  if [ "$release_created" -eq 0 ]; then
    rm -f "$create_error_file"
    echo "Created release-please package release $release_tag at $target_sha."
    return
  fi

  create_error=$(cat "$create_error_file")
  rm -f "$create_error_file"

  set +e
  release_data=$(release_json "$release_tag")
  release_status=$?
  set -e

  case "$release_status" in
    0)
      ensure_release_points_to_commit "$release_tag" "$target_sha" "$release_data"
      is_draft=$(printf '%s\n' "$release_data" | jq -r '.isDraft' | strip_carriage_returns)
      if [ "$is_draft" != "false" ]; then
        publish_existing_draft_release "$release_tag" "$version"
      else
        echo "Release $release_tag was created by another workflow."
      fi
      ;;
    1)
      printf '%s\n' "$create_error" >&2
      exit 1
      ;;
    *)
      exit "$release_status"
      ;;
  esac
}

release_has_all_cli_assets() {
  release_data=$1

  asset_names=$("${ROOT_DIR}/scripts/verify-native-cli-release-assets.sh" --list) || return 1
  for asset_name in $asset_names; do
    asset_count=$(printf '%s\n' "$release_data" | jq --arg name "$asset_name" '[.assets[]? | select(.name == $name and .size > 0)] | length' | strip_carriage_returns)
    if [ "$asset_count" -eq 0 ]; then
      return 1
    fi
  done
}

release_has_all_dispatcher_assets() {
  release_data=$1

  asset_names=$("${ROOT_DIR}/scripts/verify-dispatcher-release-assets.sh" --list) || return 1
  for asset_name in $asset_names; do
    asset_count=$(printf '%s\n' "$release_data" | jq --arg name "$asset_name" '[.assets[]? | select(.name == $name and .size > 0)] | length' | strip_carriage_returns)
    if [ "$asset_count" -eq 0 ]; then
      return 1
    fi
  done
}

cli_release_is_ready() {
  release_tag=$1

  set +e
  release_data=$(release_json "$release_tag")
  release_status=$?
  set -e

  case "$release_status" in
    0)
      is_draft=$(printf '%s\n' "$release_data" | jq -r '.isDraft' | strip_carriage_returns)
      if [ "$is_draft" != "false" ]; then
        return 1
      fi

      release_has_all_cli_assets "$release_data"
      ;;
    1)
      return 1
      ;;
    *)
      exit "$release_status"
      ;;
  esac
}

dispatcher_release_is_ready() {
  release_tag=$1

  set +e
  release_data=$(release_json "$release_tag")
  release_status=$?
  set -e

  case "$release_status" in
    0)
      is_draft=$(printf '%s\n' "$release_data" | jq -r '.isDraft' | strip_carriage_returns)
      if [ "$is_draft" != "false" ]; then
        return 1
      fi

      release_has_all_dispatcher_assets "$release_data"
      ;;
    1)
      return 1
      ;;
    *)
      exit "$release_status"
      ;;
  esac
}

wait_for_cli_release_ready() {
  release_tag=$1
  timeout_seconds=${CLI_RELEASE_WAIT_TIMEOUT_SECONDS:-600}
  interval_seconds=${CLI_RELEASE_WAIT_INTERVAL_SECONDS:-30}
  elapsed_seconds=0

  while :; do
    if cli_release_is_ready "$release_tag"; then
      if [ "$elapsed_seconds" -gt 0 ]; then
        echo "Project runner release $release_tag is now published with complete assets."
      fi
      return 0
    fi

    if [ "$elapsed_seconds" -ge "$timeout_seconds" ]; then
      return 1
    fi

    remaining_seconds=$((timeout_seconds - elapsed_seconds))
    delay_seconds=$interval_seconds
    sleep_seconds=$delay_seconds
    if [ "$delay_seconds" -le 0 ]; then
      delay_seconds=1
      sleep_seconds=1
    fi
    if [ "$delay_seconds" -gt "$remaining_seconds" ]; then
      delay_seconds=$remaining_seconds
      sleep_seconds=$delay_seconds
    fi

    echo "Project runner release $release_tag is not published with complete assets yet; waiting ${delay_seconds}s before retry."
    if [ "$sleep_seconds" -gt 0 ]; then
      sleep "$sleep_seconds"
    fi
    elapsed_seconds=$((elapsed_seconds + delay_seconds))
  done
}

wait_for_dispatcher_release_ready() {
  release_tag=$1
  timeout_seconds=${DISPATCHER_RELEASE_WAIT_TIMEOUT_SECONDS:-600}
  interval_seconds=${DISPATCHER_RELEASE_WAIT_INTERVAL_SECONDS:-30}
  elapsed_seconds=0

  while :; do
    if dispatcher_release_is_ready "$release_tag"; then
      if [ "$elapsed_seconds" -gt 0 ]; then
        echo "Dispatcher release $release_tag is now published with complete assets."
      fi
      return 0
    fi

    if [ "$elapsed_seconds" -ge "$timeout_seconds" ]; then
      return 1
    fi

    remaining_seconds=$((timeout_seconds - elapsed_seconds))
    delay_seconds=$interval_seconds
    sleep_seconds=$delay_seconds
    if [ "$delay_seconds" -le 0 ]; then
      delay_seconds=1
      sleep_seconds=1
    fi
    if [ "$delay_seconds" -gt "$remaining_seconds" ]; then
      delay_seconds=$remaining_seconds
      sleep_seconds=$delay_seconds
    fi

    echo "Dispatcher release $release_tag is not published with complete assets yet; waiting ${delay_seconds}s before retry."
    if [ "$sleep_seconds" -gt 0 ]; then
      sleep "$sleep_seconds"
    fi
    elapsed_seconds=$((elapsed_seconds + delay_seconds))
  done
}

verify_minimum_cli_release_protocol() {
  release_ref=$1

  (
    cd "$ROOT_DIR/tools/release-automation"
    go run ./cmd/check-protocol-minimum-version --verify-release --ref "$release_ref"
  )
}

release_tag_from_config() {
  package_path=$1
  version=$2

  jq -r --arg package_path "$package_path" '
    .packages[$package_path] as $package
    | [
        ($package.component // "__ULOOP_EMPTY_COMPONENT__"),
        ($package["include-component-in-tag"] // false),
        ($package["include-v-in-tag"] // false)
      ]
    | @tsv
  ' "$CONFIG" | strip_carriage_returns |
  while IFS='	' read -r component include_component include_v; do
    if [ "$component" = "__ULOOP_EMPTY_COMPONENT__" ]; then
      component=""
    fi

    release_tag_for_package "$component" "$include_component" "$include_v" "$version"
    break
  done
}

mark_package_release_sync_ready true

fetch_release_refs

cli_version=$(jq -r --arg package_path "$CLI_PACKAGE_PATH" '.[$package_path] // empty' "$MANIFEST" | strip_carriage_returns)
if [ -n "$cli_version" ] && jq -e --arg package_path "$CLI_PACKAGE_PATH" '.packages[$package_path] != null' "$CONFIG" >/dev/null; then
  cli_release_tag=$(release_tag_from_config "$CLI_PACKAGE_PATH" "$cli_version")
  if ! wait_for_cli_release_ready "$cli_release_tag"; then
    mark_package_release_sync_ready false
    echo "Project runner release $cli_release_tag is not published with complete assets; package release sync will wait."
    exit 0
  fi
  fetch_cli_release_tag "$cli_release_tag"
fi

minimum_dispatcher_version=$(jq -r '.minimumDispatcherVersion // empty' "$ROOT_DIR/$UNITY_PACKAGE_CLI_PIN_FILE" | strip_carriage_returns)
if [ -n "$minimum_dispatcher_version" ]; then
  dispatcher_release_tag="dispatcher-v$minimum_dispatcher_version"
  if ! wait_for_dispatcher_release_ready "$dispatcher_release_tag"; then
    mark_package_release_sync_ready false
    echo "Dispatcher release $dispatcher_release_tag is not published with complete assets; package release sync will wait."
    exit 0
  fi
fi

jq -r --arg cli_skip "$CLI_PACKAGE_PATH" --arg dispatcher_skip "$DISPATCHER_PACKAGE_PATH" '
  .packages
  | to_entries[]
  | select(.key != $cli_skip and .key != $dispatcher_skip)
  | [
      .key,
      (.value["changelog-path"] // ""),
      (.value.component // "__ULOOP_EMPTY_COMPONENT__"),
      (.value["include-component-in-tag"] // false),
      (.value["include-v-in-tag"] // false)
    ]
  | @tsv
' "$CONFIG" | strip_carriage_returns |
while IFS='	' read -r package_path changelog_config_path component include_component include_v; do
  if [ "$component" = "__ULOOP_EMPTY_COMPONENT__" ]; then
    component=""
  fi

  version=$(jq -r --arg package_path "$package_path" '.[$package_path] // empty' "$MANIFEST" | strip_carriage_returns)
  if [ -z "$version" ]; then
    echo "Skipping $package_path because it has no release-please manifest version."
    continue
  fi

  if [ -z "$changelog_config_path" ]; then
    echo "release-please package $package_path has no changelog-path." >&2
    exit 1
  fi

  changelog_path=$(resolve_package_path "$package_path" "$changelog_config_path")
  if [ ! -f "$changelog_path" ]; then
    echo "Missing changelog for release-please package $package_path: $changelog_path" >&2
    exit 1
  fi

  release_tag=$(release_tag_for_package "$component" "$include_component" "$include_v" "$version")
  set +e
  release_data=$(release_json "$release_tag")
  release_status=$?
  set -e

  case "$release_status" in
    0)
      release_commit_sha=$(release_commit_sha_for_package "$package_path" "$version" "$changelog_path")
      if [ -n "$release_commit_sha" ]; then
        ensure_release_points_to_commit "$release_tag" "$release_commit_sha" "$release_data"
      fi

      is_draft=$(printf '%s\n' "$release_data" | jq -r '.isDraft' | strip_carriage_returns)
      if [ "$is_draft" != "false" ]; then
        if [ -z "$release_commit_sha" ]; then
          echo "Draft release $release_tag cannot be protocol-verified because no release-please commit for $package_path version $version was found." >&2
          exit 1
        fi
        verify_minimum_cli_release_protocol "$release_commit_sha"
        publish_existing_draft_release "$release_tag" "$version"
      else
        echo "Release $release_tag already exists."
      fi
      ;;
    1)
      release_commit_sha=$(release_commit_sha_for_package "$package_path" "$version" "$changelog_path")
      if [ -z "$release_commit_sha" ]; then
        echo "Missing release $release_tag, but no release-please commit for $package_path version $version was found." >&2
        exit 1
      fi

      verify_minimum_cli_release_protocol "$release_commit_sha"
      notes_file="$TMP_DIR/$release_tag.md"
      write_release_notes "$changelog_path" "$version" "$notes_file"
      create_package_release "$release_tag" "$version" "$release_commit_sha" "$notes_file"
      ;;
    *)
      exit "$release_status"
      ;;
  esac
done
