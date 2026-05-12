#!/bin/sh
set -eu

ROOT_DIR=${ULOOP_REPO_ROOT:-$(CDPATH= cd "$(dirname "$0")/.." && pwd)}
CONFIG="$ROOT_DIR/release-please-config.json"
MANIFEST="$ROOT_DIR/.release-please-manifest.json"
CLI_PACKAGE_PATH="Packages/src/Cli~"
REPO_FULL_NAME=${GITHUB_REPOSITORY:-hatayama/unity-cli-loop}
TMP_DIR=$(mktemp -d)

cd "$ROOT_DIR"

cleanup() {
  rm -rf "$TMP_DIR"
}

trap cleanup EXIT INT HUP TERM

resolve_package_path() {
  package_path=$1
  file_path=$2

  case "$file_path" in
    /*)
      printf '%s\n' "${file_path#/}"
      ;;
    *)
      if [ "$package_path" = "." ]; then
        printf '%s\n' "$file_path"
        return
      fi

      printf '%s/%s\n' "$package_path" "$file_path"
      ;;
  esac
}

release_json() {
  release_tag=$1
  release_error_file=$(mktemp)
  if gh release view "$release_tag" --repo "$REPO_FULL_NAME" --json isDraft,targetCommitish 2>"$release_error_file"; then
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

  git log --format=%H HEAD |
  while IFS= read -r commit_sha; do
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

  git show --format= "$commit_sha" -- .release-please-manifest.json "$changelog_path" 2>/dev/null |
    awk -v manifest_entry="$expected_manifest_entry" -v changelog_heading="$expected_changelog_heading" '
      substr($0, 1, 1) == "+" && index($0, manifest_entry) > 0 {
        found = 1
      }
      substr($0, 1, 1) == "+" && index($0, changelog_heading) > 0 {
        found = 1
      }
      END {
        exit found ? 0 : 1
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

  release_target=$(printf '%s\n' "$release_data" | jq -r '.targetCommitish')
  resolved_release_target=$(git rev-parse "$release_target^{commit}" 2>/dev/null || printf '%s\n' "$release_target")
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

  gh release create "$release_tag" \
    --repo "$REPO_FULL_NAME" \
    --title "$release_tag" \
    --notes-file "$notes_file" \
    --target "$target_sha" \
    $prerelease_flag

  echo "Created release-please package release $release_tag at $target_sha."
}

if git remote get-url origin >/dev/null 2>&1; then
  git fetch --force --tags origin >/dev/null
fi

jq -r --arg skip "$CLI_PACKAGE_PATH" '
  .packages
  | to_entries[]
  | select(.key != $skip)
  | [
      .key,
      (.value["changelog-path"] // ""),
      (.value.component // "__ULOOP_EMPTY_COMPONENT__"),
      (.value["include-component-in-tag"] // false),
      (.value["include-v-in-tag"] // false)
    ]
  | @tsv
' "$CONFIG" |
while IFS='	' read -r package_path changelog_config_path component include_component include_v; do
  if [ "$component" = "__ULOOP_EMPTY_COMPONENT__" ]; then
    component=""
  fi

  version=$(jq -r --arg package_path "$package_path" '.[$package_path] // empty' "$MANIFEST")
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

      is_draft=$(printf '%s\n' "$release_data" | jq -r '.isDraft')
      if [ "$is_draft" != "false" ]; then
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

      notes_file="$TMP_DIR/$release_tag.md"
      write_release_notes "$changelog_path" "$version" "$notes_file"
      create_package_release "$release_tag" "$version" "$release_commit_sha" "$notes_file"
      ;;
    *)
      exit "$release_status"
      ;;
  esac
done
