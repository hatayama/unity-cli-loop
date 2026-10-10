# Create the Unity Package Release Only After Its Tarball Is Signed

Date: 2026-10-10

## Decision

The Unity package release (`v<version>`) is created by one workflow, `unity-package-release.yml`,
and by nothing else. A run reads the newest `main` commit. When no release of the manifest
version is published and the readiness check names its release commit, the run signs the
package at that commit, creates the tag there, and creates and publishes the release with the
signed tarball in one `gh release create` call. Runs are serialized. A run that cannot sign
creates nothing: no tag, no draft, no release.

release-please, dispatcher-publish, and native-cli-publish do not create the package release.
Their completion only starts the workflow, because each of them can be the last thing the
release waits for.

## Context

- The repository has immutable releases enabled. A published release takes no new asset
  (HTTP 422), so a signed tarball cannot be attached after publishing. The release sync that
  used to create the package release published `v3.14.0` before any tarball existed, and that
  version stays unsigned.
- OpenUPM with `trackingMode: githubRelease` finds versions by their git tags. A tag whose
  release or matching asset is missing puts the version on hold; OpenUPM rechecks it at growing
  intervals and gives up on the version after three days.
- The "Protect release tags" ruleset forbids deleting and moving tags, so a tag pointed at the
  wrong commit cannot be repaired.
- With an asset, `gh release create` creates a draft, uploads to it, and publishes it, deleting
  the draft if the upload fails. A release created that way is never published without its
  tarball.
- A draft does not own its tag. A probe on 2026-10-10 created a second draft of a tag that
  already had one, and the release listing right afterwards showed only one of them.

## Rejected Alternatives

- **Create the release as a draft in the release sync and publish it after signing.** The
  release then has two writers in four workflows. The sync runs in three workflows that are not
  serialized against each other, so two of them can each create a draft of the same tag, and the
  publishing side has to pick "the" draft from a listing that lags its writes.
- **Create the tag in the release sync and the release after signing.** The tag is visible to
  OpenUPM from the moment the sync runs, so every signing failure starts the three-day hold, and
  one that outlasts it loses the version for OpenUPM.
- **Sign in each workflow that ran the sync.** The signing credentials and the third-party UPM
  CLI would reach three workflows instead of one job that cannot write to the repository.

## Consequences

- release-please's own steps no longer wait for a readiness output. While the package release
  pull request is `autorelease: pending`, release-please (v17.3.0) aborts before opening any
  release pull request, so the gates that read that output were redundant.
- A failed run leaves that pull request pending, which stops further release pull requests
  until the release exists. The failure is reported, and a manual run with `dry-run` off
  creates the release once the cause is fixed.
- The readiness check does not wait for the runner or dispatcher release it needs. The publish
  workflow of whichever is missing starts the release workflow again when it completes.
- A run cut off between creating the tag and creating the release leaves only the tag, which
  the next run reuses; the OpenUPM hold applies only to this window. A draft left by a cut-off
  `gh release create` is deleted by the next run before it creates the release.
- After it creates a release with `GITHUB_TOKEN`, which starts no workflow, the workflow starts
  release-please so the pending pull request is marked tagged without waiting for the next push.

## Reversal Condition

Revisit when a published release can take an asset again (GitHub lifts the restriction, or the
repository turns immutable releases off), or when OpenUPM signs packages itself: either way the
release no longer has to wait for the signed tarball.
