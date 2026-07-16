# Release recovery runbook

## Root cause

In `recovery-target` mode, the release target is an earlier commit than the
workflow run's `GITHUB_SHA`. GitHub rejects the Actions `GITHUB_TOKEN` when it
tries to create a tag or release for that different commit, even when the token
has `contents: write`. This is neither a missing permission nor a tag ruleset
failure.

The behavior was confirmed with the same token and `contents: write`
permission in reproduction runs [29459199798](https://github.com/hatayama/unity-cli-loop/actions/runs/29459199798)
and [29459266044](https://github.com/hatayama/unity-cli-loop/actions/runs/29459266044):

- A tag pointing at the run head returned HTTP 201.
- A tag pointing at `2d8d1b9443843f45b48479134f846b6f540e928b`, an ancestor of
  the run head, returned HTTP 403 `Resource not accessible by integration`.
- Creating a draft release directly at that past commit also returned HTTP 403.

The precise condition is a ref target different from the run's `GITHUB_SHA`,
not merely an unreachable commit. Normal head-based releases are unaffected.

## Recovery procedure

Run these commands with an owner-authenticated `gh` session. Replace every
`<placeholder>` before running a command.

1. Confirm the authenticated account and repository.

   ```sh
   gh auth status
   gh api user --jq .login
   gh api repos/<owner>/<repo> --jq .full_name
   ```

2. Determine the release commit and release tag from the release PR and the
   failed workflow run.

   ```sh
   gh pr view <release-pr> --repo <owner>/<repo> --json mergeCommit --jq .mergeCommit.oid
   gh run view <run-id> --repo <owner>/<repo> --json headSha
   ```

   The release commit must be the commit validated by the workflow. Do not use
   the current branch head when recovering an older release.

3. Create the release tag at the validated release commit.

   ```sh
   gh api repos/<owner>/<repo>/git/refs \
     -f ref=refs/tags/<tag> \
     -f sha=<release-commit-sha>
   ```

4. Download the native release input artifact and create the draft release.
   Creating only the tag is not sufficient: the Actions token also receives
   HTTP 403 when it tries to create the draft release.

   ```sh
   gh run download <run-id> --repo <owner>/<repo> \
     -n native-cli-release-input -D <native-release-input-dir>
   gh release create <tag> --repo <owner>/<repo> \
     --draft --title <tag> \
     --notes-file <native-release-input-dir>/release-input/release-notes.md \
     --target <release-commit-sha>
   ```

   Add `--prerelease` when the release tag is a prerelease tag.

5. Rerun the failed publish job and approve the release environment if
   prompted.

   ```sh
   gh run rerun <run-id> --repo <owner>/<repo> --failed
   ```

6. If an older `dispatcher-publish` run is waiting for the `cli-release`
   environment, cancel it before retrying the recovery. The workflow uses a
   concurrency group without automatic cancellation, so an old approval-waiting
   run can block the newer run indefinitely.

   ```sh
   gh run list --repo <owner>/<repo> --workflow dispatcher-publish.yml \
     --branch <branch> --limit 20
   gh run cancel <blocked-run-id> --repo <owner>/<repo>
   ```

7. Recover the Unity package release only after the runner release has
   completed and its assets are available. The package sync script creates a
   release at the historical release commit and can hit the same 403; create
   that release as the owner before rerunning the sync.

   ```sh
   gh release create v<package-version> --repo <owner>/<repo> \
     --title v<package-version> \
     --notes-file <package-release-notes> \
     --target <package-release-commit-sha>
   ```

   Add `--prerelease` when the package release is a prerelease.

   Run the package sync or the relevant post-publish job after the runner
   release assets are complete. The sync reuses an existing correctly targeted
   release.

8. Complete the workflow-dispatch recovery manually. Push-only post-publish
   steps are intentionally skipped for a `workflow_dispatch` run.

   ```sh
   gh pr edit <release-pr> --repo <owner>/<repo> \
     --remove-label 'autorelease: pending' \
     --add-label 'autorelease: tagged'
   gh workflow run release-please.yml --repo <owner>/<repo> --ref <branch> \
     -f branch=<branch>
   ```

## Scope of the workflows

`native-cli-publish.yml` is the workflow with `recovery-target` and now emits
this runbook path when tag or draft-release creation returns the observed 403.
The creation and reuse logic is unchanged.

`dispatcher-publish.yml` has no recovery-target input and requires its release
target to equal `GITHUB_SHA`, so it has no historical-commit creation path.
Its concurrency group still matters during recovery because an older run can
block a retry.

`scripts/sync-release-please-package-releases.sh` can create package releases
at release-please commits from the repository history. This runbook's owner
pre-creation step covers that path; the runner release must be recovered first.

Do not add GitHub Actions to the tag-ruleset bypass actors. That would grant
the bot tag deletion and force-update capabilities without addressing the
historical-commit restriction.
