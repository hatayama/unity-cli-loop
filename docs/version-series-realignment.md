# Version series realignment

Use this document when a component's release-please version series must be
moved backward (for example, a component accidentally escaped the shared
`3.0.0-beta` line because a bootstrap release made release-please treat a
final version as shipped). The dispatcher realignment that shipped as
`dispatcher-v3.0.0-beta.19` (PR #1888) followed this procedure: an
unintended `dispatcher-v3.0.0` bootstrap release
had pushed the dispatcher onto a `3.1.0-beta` series, and it was rewound to
`3.0.0-beta.19`.

## Mechanics that make a rewind possible

- release-please is manifest-driven. `.release-please-manifest.json` is the
  version source of truth, and no CI guard forbids lowering a manifest entry.
- `scripts/install.sh` resolves `latest`/`latest-beta` by walking GitHub
  releases in publish-date order, not by semver. A semver-lower release
  published later is still selected as the newest, so new installs converge.
- Already-installed dispatchers converge through the periodic optional
  self-update, which reinstalls the newest published release.

## Procedure

1. Delete the unintended release and its tag (`gh release delete <tag>
   --cleanup-tag`). The "Protect release tags" ruleset blocks tag deletion for
   everyone including admins; the repository owner must temporarily disable
   the ruleset in the GitHub UI and re-enable it immediately afterwards.
   Leave every other historical release in place — old package pins verify
   their `minimumDispatcherVersion` against existing release tags.
2. In one commit, set the target version consistently across:
   - the component's entry in `.release-please-manifest.json`,
   - the component's version declaration (for the dispatcher,
     `dispatchercontract/dispatcher-contract.json` — normally stamped by
     release-please only; a realignment is the one legitimate manual edit),
   - a matching `## [<version>]` heading at the top of the component's
     `CHANGELOG.md`,
   - both pin files' `minimumDispatcherVersion` when the dispatcher is
     involved. The dispatcher minimum version guard passes without a release
     lookup when the pin minimum equals the in-tree `dispatcherVersion`.
3. Merge the PR with an ordinary `fix:` title and let the publish automation
   self-heal (see below). Do not create the missing release or tag by hand.

## How the missing release gets created

Two automations react differently to the realignment commit:

- `scripts/sync-release-please-package-releases.sh` only recognizes release
  commits whose subject passes `scripts/is-release-please-release-commit.sh`
  (`chore: release *` / `chore(...): release *`). A `fix:`-titled realignment
  commit is invisible to it, so it cannot create the missing release and may
  fail with "no release-please commit found" until the release exists.
- `.github/workflows/dispatcher-publish.yml`
  (`scripts/resolve-dispatcher-release-target.sh`) ignores commit subjects
  entirely, but on push it only evaluates when HEAD's diff actually stamps
  the resolved `dispatcherVersion` — a `+` line matching
  `"dispatcherVersion": "<version>"` in the contract or a
  `## [<version>]` heading in `cli/dispatcher/CHANGELOG.md`
  (`release_commit_updates_dispatcher_version`). A `fix:`-titled realignment
  commit still stamps both the contract and the CHANGELOG, so it passes this
  gate even though its subject doesn't look like a release commit. When the
  gate passes and the resolved release is missing or lacks assets, the
  workflow builds the binaries and creates/publishes the release with
  attestations at the pushed commit. This is what materializes the realigned
  version as a real release.

If the realignment push doesn't stamp the version yet, or the
release-please workflow failed before dispatcher-publish finished, retry via
`workflow_dispatch` (which bypasses the push gate and falls back to the
state-based evaluation) once the release exists with all assets.

## What not to touch

- Never hand-edit `dispatcherReleaseTag` or `dispatcherArchiveManifest` in the
  pin files. `VerifyDispatcherPinSubjects` requires the pinned manifest to
  match the published release's verified subjects exactly, so pointing them at
  a not-yet-published tag cannot pass CI, and the archive hashes are unknowable
  before the release exists. The pin stamp automation moves them on the next
  normal dispatcher release (see `docs/dispatcher-pin-release-order.md`).
- Do not add an empty commit just to see the next version proposed. The
  realignment commit itself becomes the tagged release commit, so release-please
  correctly proposes nothing for the component until the next real change under
  its package root.
