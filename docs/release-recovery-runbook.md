# Release recovery runbook

## Purpose

Use this runbook when a native CLI release has a tag commit that differs from
the `sourceRepositoryDigest` in its asset attestations. Such a release cannot
be downloaded by the dispatcher. Recover by publishing the next valid release;
do not try to repair the broken release in place.

## First response: roll forward

1. Confirm the affected release is broken by comparing its tag commit with an
   attached asset attestation.

   ```sh
   gh release download <release-tag> --repo <owner>/<repo> \
     --pattern <asset-name> --dir <download-directory>
   gh api repos/<owner>/<repo>/commits/<release-tag> --jq .sha
   gh attestation verify <download-directory>/<asset-name> --repo <owner>/<repo> --format json \
     | jq -r '.[].verificationResult.signature.certificate.sourceRepositoryDigest'
   ```

2. Merge the pending release-please release PR for the affected branch. This
   creates the next version and starts a normal head-based publish run.

3. Verify the new runner release and the package release that pins it. The
   runner tag commit, every asset's attestation digest, and the publish run's
   `headSha` must match.

   ```sh
   gh run view <publish-run-id> --repo <owner>/<repo> --json headSha --jq .headSha
   gh api repos/<owner>/<repo>/commits/<new-runner-tag> --jq .sha
   gh attestation verify <new-asset> --repo <owner>/<repo> --format json \
     | jq -r '.[].verificationResult.signature.certificate.sourceRepositoryDigest'
   ```

## Why a broken release cannot be revived

Do not create a new tag, draft, or publish run for the historical version.
Revival is structurally impossible for three independent reasons:

1. Rerunning a workflow replays the workflow definition from the original
   commit. If that workflow definition was itself broken, rerunning it repeats
   the failure.
2. A new workflow run attests its own `github.sha`, which is the current branch
   head. It cannot create attestations for an earlier release commit.
3. The current workflow permits a release target only when it equals the
   approved event commit. A new run for a historical version therefore fails
   before it can create a tag or publish assets.

Reusing an affected version cannot repair this invariant: its tag and asset
attestations would still identify different commits. Recovery must move to a
new version whose release data agrees.

## When rerun recovery is valid

Rerun only when all of the following are true:

- The original failed run's `headSha` is the intended release commit.
- The workflow definition at that commit is known to be healthy.
- The release tag and draft release, if they exist, point at that same commit.

Check the original run before rerunning it:

```sh
gh run view <run-id> --repo <owner>/<repo> --json headSha,workflowName,url
gh api repos/<owner>/<repo>/commits/<release-tag> --jq .sha
gh run view <run-id> --repo <owner>/<repo> --log-failed
```

If the run used an older workflow revision that lacks a required fix, or its
head SHA differs from the intended release commit, do not rerun it. Use the
roll-forward procedure instead.

When all conditions hold, rerun the failed jobs from the original run and
approve the `cli-release` environment if GitHub requests approval:

```sh
gh run rerun <run-id> --repo <owner>/<repo> --failed
```

## Retrying after a cancelled or failed publish run

`native-cli-publish.yml` and `dispatcher-publish.yml` only evaluate publish/
release on push when HEAD's diff actually stamps the resolved version (the
release-please manifest/contract entry or the component's `CHANGELOG.md`
heading). Once a publish run is cancelled or fails, ordinary follow-up pushes
to main/v3-beta no longer retry it, because their HEAD commits do not stamp
that version. Recovery is limited to two paths:

- (a) Rerun the original run, but only when the conditions in "When rerun
  recovery is valid" above hold (its `headSha` is the intended release commit
  and the workflow definition there is healthy).
- (b) Trigger the workflow manually via `workflow_dispatch`, which bypasses
  the push gate entirely and falls back to the same state-based evaluation
  (missing release / missing assets / fully published) the gate used before
  this change.

## Operational notes

- Do not start a new workflow run to publish a historical release. A later run
  cannot produce valid attestations for an earlier commit.
- Cancel an older run waiting for `cli-release` approval before retrying. The
  workflow concurrency group does not cancel it automatically, so it can block
  the newer run indefinitely.
- Delete a broken release only after the version bump is merged. Deleting it
  first leaves the historical version unresolved; later push builds will not
  attempt to recreate it on their own (see "Retrying after a cancelled or
  failed publish run" above), so recovery still requires an explicit rerun or
  `workflow_dispatch`.
- Do not grant GitHub Actions tag-ruleset bypass permissions. That does not
  resolve the attestation invariant and expands bot authority unnecessarily.
- A broken release may remain published while the roll-forward release is cut;
  the dispatcher rejects it, so it is harmless once consumers use the newer
  package pin.
