#!/bin/sh
set -eu

ROOT_DIR=$(CDPATH= cd "$(dirname "$0")/.." && pwd)
WORKFLOW="$ROOT_DIR/.github/workflows/unity-package-release.yml"
WORKFLOWS_DIR="$ROOT_DIR/.github/workflows"
FAILURE_NOTIFY_WORKFLOW="$ROOT_DIR/.github/workflows/release-failure-notify.yml"

fail() {
  echo "$1" >&2
  exit 1
}

# job_section prints one job of the workflow, from its key to the next job key.
job_section() {
  job_name=$1
  awk -v job_key="  $job_name:" '
    $0 == job_key { printing = 1; print; next }
    printing && /^  [A-Za-z0-9_-]+:$/ { exit }
    printing { print }
  ' "$WORKFLOW"
}

assert_job_contains() {
  job_name=$1
  expected=$2
  if ! job_section "$job_name" | grep -F -- "$expected" >/dev/null 2>&1; then
    fail "Expected job $job_name to contain: $expected"
  fi
}

assert_job_not_contains() {
  job_name=$1
  unexpected=$2
  if job_section "$job_name" | grep -F -- "$unexpected" >/dev/null 2>&1; then
    fail "Expected job $job_name not to contain: $unexpected"
  fi
}

workflow_run_trigger() {
  awk '/^  workflow_run:$/ { inside = 1; next } /^  [a-z_]+:$/ { inside = 0 } inside' "$WORKFLOW"
}

# Verifies the workflow follows, on main only, every workflow whose completion can make the package release due.
test_runs_after_each_dependency_on_main() {
  for workflow_name in release-please dispatcher-publish native-cli-publish; do
    if ! workflow_run_trigger | grep -Fx -- "      - $workflow_name" >/dev/null; then
      fail "Expected the workflow_run trigger to list $workflow_name."
    fi
  done
  if ! workflow_run_trigger | awk '/^    branches:$/ { inside = 1; next } /^    [a-z_-]+:$/ { inside = 0 } inside' | grep -Fx -- "      - main" >/dev/null; then
    fail "Expected the workflow_run trigger to follow main only."
  fi
}

# Verifies only the job that creates the release is serialized, so a run that skips or only signs never takes the place of a waiting creation, and a running creation is never cancelled.
test_only_release_creations_run_one_at_a_time() {
  if grep -n '^concurrency:' "$WORKFLOW"; then
    fail "Expected no workflow-level concurrency group: every run would then queue in it, including runs that never create a release."
  fi
  assert_job_contains publish "      group: unity-package-release"
  assert_job_contains publish "      cancel-in-progress: false"
}

# Verifies each job holds only the permissions it needs: the plan and the signing job only read, only publish writes contents, and only post-publish edits pull requests and starts workflows.
test_jobs_use_least_privilege() {
  assert_job_contains plan "      contents: read"
  assert_job_not_contains plan ": write"
  assert_job_contains sign "      contents: read"
  assert_job_not_contains sign ": write"
  assert_job_contains publish "      contents: write"
  assert_job_not_contains publish "actions: write"
  assert_job_not_contains publish "pull-requests: write"
  assert_job_contains post-publish "      actions: write"
  assert_job_contains post-publish "      pull-requests: write"
  assert_job_not_contains post-publish "contents: write"
}

# Verifies the signing credentials reach only the signing job, which runs in the main-only environment and cannot write to the repository.
test_signing_credentials_stay_in_the_signing_job() {
  assert_job_contains sign "    environment: upm-signing"
  for job_name in plan publish post-publish; do
    assert_job_not_contains "$job_name" '${{ secrets.'
    assert_job_not_contains "$job_name" "environment:"
  done
}

# Verifies the release is created only after signing succeeded and only when the plan says so.
test_release_is_created_only_after_signing() {
  assert_job_contains publish "    needs: [plan, sign]"
  assert_job_contains publish "    if: needs.plan.outputs.publish == 'true'"
  assert_job_contains publish "go run ./cmd/create-unity-package-release"
  assert_job_contains publish '          RELEASE_COMMIT: ${{ needs.plan.outputs.source-ref }}'
  assert_job_contains sign "    if: needs.plan.outputs.sign == 'true'"
}

# Verifies the manual run's dry-run choice reaches the plan, which is the only thing that turns publish off for a dry run: without it the plan cannot tell a dry run from a run that creates the immutable release.
test_dry_run_reaches_the_plan() {
  assert_job_contains plan "          DRY_RUN: \${{ github.event_name == 'workflow_dispatch' && inputs.dry-run }}"
  assert_job_contains plan '--dry-run="$DRY_RUN"'
}

# Verifies every plan decision reaches the jobs that act on it, and that the package is signed from the commit the plan named; a missing job output reads as empty, which silently skips the job or signs the workflow ref instead.
test_plan_decisions_reach_the_jobs() {
  for output_name in sign publish tag version asset-name source-ref; do
    assert_job_contains plan "      $output_name: \${{ steps.plan.outputs.$output_name }}"
  done
  assert_job_contains sign '          ref: ${{ needs.plan.outputs.source-ref }}'
  assert_job_contains sign "          name: signed-unity-package"
  assert_job_contains publish "          name: signed-unity-package"
}

# Verifies post-publish marks the release pull request tagged itself before it starts release-please: release-please skips its own label sync while the release commit is the newest main commit.
test_post_publish_marks_the_release_pull_request_tagged() {
  assert_job_contains post-publish "    needs: [plan, publish]"
  assert_job_contains post-publish '          RELEASE_TAG: ${{ needs.plan.outputs.tag }}'
  assert_job_contains post-publish '          TARGET_SHA: ${{ needs.plan.outputs.source-ref }}'
  assert_job_contains post-publish "          TARGET_BRANCH: main"
  mark_line=$(job_section post-publish | grep -n -F -- "run: scripts/mark-release-pr-tagged.sh" | cut -d: -f1)
  dispatch_line=$(job_section post-publish | grep -n -F -- 'run: gh workflow run release-please.yml --repo "$GITHUB_REPOSITORY" --ref main -f branch=main' | cut -d: -f1)
  [ -n "$mark_line" ] || fail "Expected post-publish to mark the release pull request tagged."
  [ -n "$dispatch_line" ] || fail "Expected post-publish to start release-please."
  [ "$mark_line" -lt "$dispatch_line" ] || fail "Expected the pull request to be marked tagged before release-please starts."
}

# Verifies no workflow still reads the readiness output or runs the release sync that this workflow replaced; an if: on a missing output reads as false and silently skips the step.
test_no_workflow_uses_the_removed_release_sync() {
  if grep -rnF -e "outputs.ready" -e "package_release_sync" -e "sync-release-please-package-releases" "$WORKFLOWS_DIR"; then
    fail "Expected no workflow to use the removed package release sync."
  fi
}

# Verifies a failed run opens the release failure issue.
test_failures_are_reported() {
  if ! awk '/^  workflow_run:$/ { inside = 1; next } /^[a-z]/ { inside = 0 } inside' "$FAILURE_NOTIFY_WORKFLOW" | grep -Fx -- "      - unity-package-release" >/dev/null; then
    fail "Expected release-failure-notify to follow unity-package-release."
  fi
}

test_runs_after_each_dependency_on_main
test_only_release_creations_run_one_at_a_time
test_jobs_use_least_privilege
test_signing_credentials_stay_in_the_signing_job
test_release_is_created_only_after_signing
test_dry_run_reaches_the_plan
test_plan_decisions_reach_the_jobs
test_post_publish_marks_the_release_pull_request_tagged
test_no_workflow_uses_the_removed_release_sync
test_failures_are_reported
