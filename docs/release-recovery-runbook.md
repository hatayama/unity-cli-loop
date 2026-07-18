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

The 2026-07-17 recovery followed this procedure: runner beta.48 was left
broken, the next release-please PR produced runner beta.49, and the following
package release pinned beta.49. The normal publish run, tag, and attestations
all resolved to the same commit.

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

For example, beta.48's tag pointed at `2d8d1b94` while its published asset
attestations carried `2c73c6ac`. Reusing that version would preserve the same
invariant violation.

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

## Operational notes

### Preserved negative attestation specimen

Do not delete `uloop-project-runner-v3.0.0-beta.48`. It is an intentionally
preserved broken release: its tag commit is `2d8d1b94`, while its asset
attestations carry `2c73c6ac`.

This is the only current production specimen that can prove attestation
mismatches fail against real release data. Use it to verify the publish
workflow digest check, sync's `verify_cli_release_attestations`, and the
dispatcher download-time verification reject a mismatched release. Do not pin
this version; use beta.49 or later.

- Do not start a new workflow run to publish a historical release. A later run
  cannot produce valid attestations for an earlier commit.
- Cancel an older run waiting for `cli-release` approval before retrying. The
  workflow concurrency group does not cancel it automatically, so it can block
  the newer run indefinitely.
- Delete a broken release only after the version bump is merged. Deleting it
  first leaves the historical version unresolved and can make later push builds
  attempt to recreate it and fail.
- Do not grant GitHub Actions tag-ruleset bypass permissions. That does not
  resolve the attestation invariant and expands bot authority unnecessarily.
- A broken release may remain published while the roll-forward release is cut;
  the dispatcher rejects it, so it is harmless once consumers use the newer
  package pin.
