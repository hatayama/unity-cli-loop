#!/bin/sh
set -eu

# Says whether the Unity package release of the release-please manifest version
# can be created now. When it can, stdout carries the release commit and
# nothing else; when a release it depends on is not published yet, stdout is
# empty and a notice says which. A release commit that fails its protocol or
# pin check, a failed lookup, or an unreadable manifest or config fails the
# script instead, because none of them resolves by waiting.
#
# The caller reads stdout as the commit, so file descriptor 3 keeps the real
# stdout and every command below writes its own stdout to stderr.
exec 3>&1 1>&2

ROOT_DIR=${ULOOP_REPO_ROOT:-$(CDPATH= cd "$(dirname "$0")/.." && pwd)}
# SCRIPT_DIR must resolve from $0, not ROOT_DIR: ULOOP_REPO_ROOT may point at a
# repository that does not contain the helper scripts this script calls.
SCRIPT_DIR=$(CDPATH= cd "$(dirname "$0")" && pwd)
CONFIG="$ROOT_DIR/release-please-config.json"
MANIFEST="$ROOT_DIR/.release-please-manifest.json"
CLI_PACKAGE_PATH="cli/project-runner"
UNITY_PACKAGE_PATH="Packages/src"
UNITY_PACKAGE_CLI_PIN_FILE="Packages/src/project-runner-pin.json"
REPO_FULL_NAME=${GITHUB_REPOSITORY:-hatayama/unity-cli-loop}
TMP_DIR=$(mktemp -d)

cd "$ROOT_DIR"

cleanup() {
  rm -rf "$TMP_DIR"
}

trap cleanup EXIT INT HUP TERM

strip_carriage_returns() {
  tr -d '\015'
}

# json_value prints one jq result of a JSON file. A missing or unparsable file
# fails the script: piping jq straight into another command would hide that
# failure and read as "no value", which looks like a release that is not due.
json_value() {
  json_file=$1
  shift
  json_result=$(jq -r "$@" "$json_file") || return 1
  printf '%s\n' "$json_result" | strip_carriage_returns
}

# report_not_due ends the check with empty stdout. Whatever is not published
# yet starts the Unity package release workflow again when its own publish
# workflow completes, so the check does not wait for it.
report_not_due() {
  printf '::notice::%s\n' "$1"
  exit 0
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
  if gh release view "$release_tag" --repo "$REPO_FULL_NAME" --json isDraft,assets 2>"$release_error_file"; then
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
  # The history is read into a file first: piped into the loop, a failed read
  # would only look like a history without the release commit.
  commit_log_file="$TMP_DIR/release-commit-log.txt"
  git log --format='%H%x09%s' HEAD > "$commit_log_file" || return 1
  while IFS='	' read -r commit_sha commit_subject; do
    if ! "$SCRIPT_DIR/is-release-please-release-commit.sh" "$commit_subject"; then
      continue
    fi
    if release_commit_updates_package_version "$commit_sha" "$package_path" "$version" "$changelog_path"; then
      printf '%s\n' "$commit_sha"
      break
    fi
  done < "$commit_log_file"
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

release_tag_from_config() {
  package_path=$1
  version=$2

  component=$(json_value "$CONFIG" --arg package_path "$package_path" '.packages[$package_path].component // empty')
  include_component=$(json_value "$CONFIG" --arg package_path "$package_path" '.packages[$package_path]["include-component-in-tag"] // false')
  include_v=$(json_value "$CONFIG" --arg package_path "$package_path" '.packages[$package_path]["include-v-in-tag"] // false')
  release_tag=""

  if [ "$include_component" = "true" ]; then
    if [ -z "$component" ]; then
      echo "release-please package $package_path has include-component-in-tag=true without a component." >&2
      exit 1
    fi

    release_tag="$component-"
  fi

  if [ "$include_v" = "true" ]; then
    release_tag="${release_tag}v"
  fi

  printf '%s%s\n' "$release_tag" "$version"
}

fetch_release_tags() {
  if git remote get-url origin >/dev/null 2>&1; then
    git fetch --force origin '+refs/tags/*:refs/tags/*'
  fi
}

# Required asset lists grow over time, and a published release only ships the
# assets its own generation required. Reading the list from HEAD would demand
# assets an older release (for example the pinned dispatcher floor) never had,
# so that release would never look ready. A release without a local tag is
# being cut from HEAD right now, so HEAD's list is correct for it.
release_asset_requirements() {
  verify_script_path=$1
  release_tag=$2
  tag_script_file="$TMP_DIR/$release_tag-$(basename "$verify_script_path")"

  if git show "refs/tags/$release_tag:$verify_script_path" > "$tag_script_file" 2>/dev/null; then
    sh "$tag_script_file" --list
    return
  fi

  "$ROOT_DIR/$verify_script_path" --list
}

release_has_all_assets() {
  release_data=$1
  release_tag=$2
  verify_script_path=$3

  asset_names=$(release_asset_requirements "$verify_script_path" "$release_tag") || return 1
  for asset_name in $asset_names; do
    asset_count=$(printf '%s\n' "$release_data" | jq --arg name "$asset_name" '[.assets[]? | select(.name == $name and .size > 0)] | length' | strip_carriage_returns)
    if [ "$asset_count" -eq 0 ]; then
      return 1
    fi
  done
}

# release_is_published_with_assets exits the script on a lookup failure other
# than "not found", because an outage must not read as "not published yet".
release_is_published_with_assets() {
  release_tag=$1
  verify_script_path=$2

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

      release_has_all_assets "$release_data" "$release_tag" "$verify_script_path"
      ;;
    1)
      return 1
      ;;
    *)
      exit "$release_status"
      ;;
  esac
}

fail_cli_release_attestation() {
  asset_name=$1

  printf 'Project runner release attestation is invalid for %s. See docs/release-recovery-runbook.md for recovery steps.\n' "$asset_name" >&2
  exit 1
}

verify_cli_release_attestations() {
  release_tag=$1
  verification_directory="$TMP_DIR/$release_tag-attestations"
  signer_workflow="$REPO_FULL_NAME/.github/workflows/native-cli-publish.yml"

  mkdir "$verification_directory"
  tag_digest_file="$verification_directory/tag-digest.txt"
  gh api "repos/$REPO_FULL_NAME/commits/$release_tag" --jq '.sha' > "$tag_digest_file"
  tag_digest=$(strip_carriage_returns < "$tag_digest_file")
  [ -n "$tag_digest" ] || fail_cli_release_attestation "$release_tag"

  asset_names=$(release_asset_requirements "scripts/verify-native-cli-release-assets.sh" "$release_tag") || fail_cli_release_attestation "$release_tag"
  for asset_name in $asset_names; do
    asset_directory="$verification_directory/$asset_name"
    bundle_name="$asset_name.sigstore.json"
    asset_path="$asset_directory/$asset_name"
    bundle_path="$asset_directory/$bundle_name"
    verification_output_path="$asset_directory/verification.json"
    verification_count_path="$asset_directory/verification-count.txt"
    digest_count_path="$asset_directory/digest-count.txt"
    digest_output_path="$asset_directory/digests.txt"
    normalized_digest_output_path="$asset_directory/normalized-digests.txt"

    mkdir "$asset_directory"
    gh release download "$release_tag" --repo "$REPO_FULL_NAME" --pattern "$asset_name" --pattern "$bundle_name" --dir "$asset_directory"
    [ -s "$asset_path" ] || fail_cli_release_attestation "$asset_name"
    [ -s "$bundle_path" ] || fail_cli_release_attestation "$asset_name"
    gh attestation verify "$asset_path" --repo "$REPO_FULL_NAME" --bundle "$bundle_path" --signer-workflow "$signer_workflow" --format json > "$verification_output_path"
    jq 'length' "$verification_output_path" > "$verification_count_path"
    jq '[.[].verificationResult.signature.certificate.sourceRepositoryDigest | select(type == "string" and length > 0)] | length' "$verification_output_path" > "$digest_count_path"
    verification_count=$(strip_carriage_returns < "$verification_count_path")
    digest_count=$(strip_carriage_returns < "$digest_count_path")
    if [ "$verification_count" -eq 0 ] || [ "$verification_count" -ne "$digest_count" ]; then
      fail_cli_release_attestation "$asset_name"
    fi
    jq -r '.[].verificationResult.signature.certificate.sourceRepositoryDigest' "$verification_output_path" > "$digest_output_path"
    strip_carriage_returns < "$digest_output_path" > "$normalized_digest_output_path"
    while IFS= read -r digest; do
      if [ "$digest" != "$tag_digest" ]; then
        fail_cli_release_attestation "$asset_name"
      fi
    done < "$normalized_digest_output_path"
  done
}

verify_minimum_cli_release_protocol() {
  release_ref=$1

  (
    cd "$ROOT_DIR/cli/release-automation"
    go run ./cmd/check-protocol-minimum-version --verify-release --ref "$release_ref"
  )
}

# The Unity package release commit is the commit its tag points at, so the pin
# recorded there is the dispatcher every fresh install of the release will
# fetch. A mismatch is failed rather than waited out: the commit's pin never
# changes, so only a later release commit can resolve it.
verify_package_pin_consistency() {
  release_ref=$1

  (
    cd "$ROOT_DIR/cli/release-automation"
    go run ./cmd/check-package-pin-consistency --repo-root "$ROOT_DIR" --ref "$release_ref"
  )
}

fetch_release_tags

version=$(json_value "$MANIFEST" --arg package_path "$UNITY_PACKAGE_PATH" '.[$package_path] // empty')
if [ -z "$version" ]; then
  echo "The release-please manifest has no version for $UNITY_PACKAGE_PATH." >&2
  exit 1
fi

changelog_config_path=$(json_value "$CONFIG" --arg package_path "$UNITY_PACKAGE_PATH" '.packages[$package_path]["changelog-path"] // empty')
if [ -z "$changelog_config_path" ]; then
  echo "release-please package $UNITY_PACKAGE_PATH has no changelog-path." >&2
  exit 1
fi

changelog_path=$(resolve_package_path "$UNITY_PACKAGE_PATH" "$changelog_config_path")
if [ ! -f "$changelog_path" ]; then
  echo "Missing changelog for release-please package $UNITY_PACKAGE_PATH: $changelog_path" >&2
  exit 1
fi

cli_version=$(json_value "$MANIFEST" --arg package_path "$CLI_PACKAGE_PATH" '.[$package_path] // empty')
cli_configured=$(json_value "$CONFIG" --arg package_path "$CLI_PACKAGE_PATH" '.packages[$package_path] != null')
if [ -n "$cli_version" ] && [ "$cli_configured" = "true" ]; then
  cli_release_tag=$(release_tag_from_config "$CLI_PACKAGE_PATH" "$cli_version")
  if ! release_is_published_with_assets "$cli_release_tag" "scripts/verify-native-cli-release-assets.sh"; then
    report_not_due "Project runner release $cli_release_tag is not published with complete assets yet; the Unity package release v$version waits for it."
  fi
  if git remote get-url origin >/dev/null 2>&1; then
    git fetch --force origin "refs/tags/$cli_release_tag:refs/tags/$cli_release_tag"
  fi
  verify_cli_release_attestations "$cli_release_tag"
fi

minimum_dispatcher_version=$(json_value "$ROOT_DIR/$UNITY_PACKAGE_CLI_PIN_FILE" '.minimumDispatcherVersion // empty')
if [ -n "$minimum_dispatcher_version" ]; then
  dispatcher_release_tag="dispatcher-v$minimum_dispatcher_version"
  if ! release_is_published_with_assets "$dispatcher_release_tag" "scripts/verify-dispatcher-release-assets.sh"; then
    report_not_due "Dispatcher release $dispatcher_release_tag is not published with complete assets yet; the Unity package release v$version waits for it."
  fi
fi

release_commit_sha=$(release_commit_sha_for_package "$UNITY_PACKAGE_PATH" "$version" "$changelog_path")
if [ -z "$release_commit_sha" ]; then
  echo "No release-please commit for $UNITY_PACKAGE_PATH version $version was found." >&2
  exit 1
fi

verify_minimum_cli_release_protocol "$release_commit_sha"
verify_package_pin_consistency "$release_commit_sha"

echo "Unity package release v$version can be created from release commit $release_commit_sha."
printf '%s\n' "$release_commit_sha" >&3
