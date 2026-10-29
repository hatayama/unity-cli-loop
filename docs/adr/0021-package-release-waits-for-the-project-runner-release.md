# The Package Release Waits for the Project Runner Release

Date: 2026-10-29

## Decision

The Unity package is released only after the project runner its pin names is published, and
release-please runs only once every release the manifest names is published.

- Release order: merge the dispatcher release pull request and approve `cli-release`; then merge
  the project runner release pull request and approve `cli-release`; the unity-package release
  pull request is merged by automation once both are published. Merging the runner first does not
  get stuck, but dispatcher then runner is the recommended order.
- `merge-package-release-pr` leaves the package release pull request draft while a project runner
  release pull request is open, or while the release `uloop-project-runner-v<projectRunnerVersion>`
  the head pin names is missing or still a draft. It checks this only after the head pin records
  the current dispatcher, and it settles the pass instead of polling, on both paths
  (`dispatcher-publish` post-publish and `release-please.yml`): the runner is published by another,
  approval-gated workflow, whose post-publish dispatches release-please, and that run's merge step
  decides again. A head that still pins the previous dispatcher is waited out only while
  release-please can rebuild it; while a release `main`'s manifest names is unpublished (a merged
  project runner release pull request still awaiting approval), the pass settles too, because that
  publish dispatches release-please.
- `check-release-please-gate` replaces the "the newest commit is a release commit" skip. It reads
  the manifest at the target commit and skips release-please while any release it names is
  missing or still a draft. A release that cannot be read fails the run rather than choosing a
  side.
- The native CLI post-publish marks its release pull request tagged before it dispatches
  release-please, as the Unity package release already does, so the dispatched run finds the label
  set.

## Context

On 2026-10-29, Unity package 3.21.2 was merged before the project runner 3.12.3 release pull
request. The runner release pull request raises `projectRunnerVersion` in the package pin
through `extra-files`, so package 3.21.2 was published pinning runner 3.12.2, and the runner fixes
made for it never reached its users. The package merge gate only checked the dispatcher.

Merging the runner first did not work either. `release-please.yml` skipped every run whose newest
commit was a release commit (#1088), including the run the runner's post-publish dispatches after
publishing ("Release commit detected; release-please will wait for the next non-release commit.").
The remaining release pull requests stayed in conflict on adjacent manifest lines and were never
rebuilt. ADR 0013 relied on the same skip when it had the package release mark its pull request
tagged itself.

The skip exists because release-please finds the previous release by tag: on a release commit
whose release is not published yet it would repeat the previous changelog entries. "The newest
commit is a release commit" was only a stand-in for "a version the manifest names is not
published yet", and the stand-in also blocked the runs that come after publishing.

An empty commit cannot be used to release only the package: release-please's manifest mode hands
a commit that touches no file to every component (`includeEmpty`).

## Considered Options

- Change only the merge order people follow (runner first): rejected. The release-commit skip
  stops the dispatched release-please run, so the remaining release pull requests are never
  rebuilt.
- Add a force input to the release-please dispatch: rejected. A dispatch by hand would still be
  able to run on a release commit whose release is pending and repeat the previous changelog
  (#1088).
- Stamp the runner into the package release pull request's pin before the runner is published:
  rejected. A pin naming an unpublished runner resolves to a 404 for every fresh install.

## Consequences

- When the runner has unreleased changes, the package release waits for one more runner approval.
- release-please runs on a release commit once every manifest release is published, so the publish
  workflows' dispatches rebuild the remaining release pull requests.
- An unreadable release fails `release-please.yml` or the merge step instead of being skipped;
  someone has to look at it.

## Revisit When

- (a) The runner and the package go back to one release pull request.
- (b) The pin's `projectRunnerVersion` comes to mean a minimum version rather than the exact runner
  the package ships with.
