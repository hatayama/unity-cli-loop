# Separate Release Pull Requests and Package Auto-Merge

Date: 2026-09-08

## Decision

release-please opens one release pull request per component
(`separate-pull-requests: true`). A human merges the `dispatcher` release pull
request and approves the `cli-release` environment; the `uloop-project-runner`
one can be merged at any time. The `unity-package` release pull request is
merged by automation: after `post-publish` pushes the dispatcher pin stamp to
`main`, `merge-package-release-pr` waits for that pull request to record the
newly published dispatcher tag, leave draft, and show a successful run of every
required workflow for its exact head commit, then merges it with
`--match-head-commit`.

The config also sets `always-update: true`. release-please otherwise pushes a
release branch only when the pull request body changes, and the pin stamp is a
`chore` commit that reaches no changelog; the unity-package pull request would
never move onto the stamp, and would keep the `.release-please-manifest.json`
conflict that merging the dispatcher release pull request creates.

`check-package-pin-consistency` makes the rule enforceable rather than
advisory. It fails any ref whose `.release-please-manifest.json` releases a
different dispatcher than `Packages/src/project-runner-pin.json` records, and
runs both on the unity-package release branch (the `check-package-release-pin`
job) and inside `sync-release-please-package-releases.sh` before the package
release is created or published.

## Context

A single release pull request bundles all three components into one merge
commit. The Unity package tag is created against that commit, and
`sync-release-please-package-releases.sh` fixes the release target to it, so
whatever pin the commit carries is what the release ships. The new dispatcher
does not exist yet at that moment: it is built after the merge, and the pin is
stamped after it is published. The package therefore always released with the
*previous* dispatcher — observed concretely with package `v3.5.0`, whose pin
recorded `dispatcher-v3.3.1` while `dispatcher-v3.4.0` was published minutes
later. Every fresh install of the package spent a whole release cycle on a
superseded dispatcher.

Delaying the tag does not help, because the commit the tag points at is fixed;
only a *later* commit can carry the newer pin. That is what makes splitting the
pull requests necessary rather than cosmetic.

ADR 0005 rejected auto-merging a pull request for the pin stamp. The situation
here is different in the three respects that made that rejection right: the
release pull request already exists and is not created for this purpose, so
nothing new produces `action_required` runs; the App token already exists for
the stamp push; and the added machinery is a single wait-and-merge step rather
than a new pull request flow. ADR 0005's note that `post-publish` no longer
needs `pull-requests: write` still holds for the job's `GITHUB_TOKEN`
permissions — the merge uses the App token, whose installation permissions now
include Pull requests write.

The merge cannot use `GITHUB_TOKEN`: a merge made with it starts no follow-up
workflow run, so `release-please.yml` would never run and the package release
would never be tagged.

## Alternatives considered

- **Write only the dispatcher version into the pin ahead of time**: the pin
  records attestation-verified asset digests, which do not exist until the
  dispatcher release is built and published. There is nothing to write early.
- **Delay only the package tag creation until after the stamp**: the sync
  resolves the release commit from the release-please release commit and pins
  the release target to it, so a later tag still points at the same stale pin.
  Changing that would mean releasing the package from a commit that is not its
  own release commit.
- **Keep one release pull request and rely on humans to merge in the right
  order**: there is no order that works — the components share one merge commit.
  Even where ordering was possible, the previous release cycle demonstrated that
  the intended order is not what happens in practice.
- **Trust "not draft" as the merge signal**: release-please moving the pull
  request head and the check automation re-drafting it are separate steps, so a
  rebased head is briefly non-draft with no checks of its own. The App is a
  ruleset bypass actor, so branch protection would not catch it either.

## Consequences

- Three release pull requests are open per cycle instead of one. The release PR
  check automation, the published-label sync, and the body clarifier all handle
  a per-component listing; a failing component no longer blocks the others.
- The Unity package release now ships the dispatcher released alongside it. The
  Install CLI button no longer lags a generation.
- The GitHub App needs **Pull requests: Read and write** in addition to
  **Contents: Read and write**, and the permission change must be approved on
  the installation. Without it the merge step fails with 403; the stamp has
  already landed at that point, so recovery is merging the pull request by hand
  after verifying the same conditions the automation checks
  (`docs/dispatcher-pin-release-order.md`).
- `always-update: true` rebases every pending release pull request on each push
  to `main`, so their checks are re-dispatched each time. That is more CI work
  per push, accepted because without it the package release pull request never
  reaches a mergeable state at all.
- release-please's pull request update path is known to fail with a 422
  "a pull request already exists" in some repositories using separate release
  pull requests (upstream issue 2773). Its reported cause is the existing-pull-request
  lookup missing on a `head.label` mismatch; this repository's owner login
  matches that label, and the same update path already refreshes the combined
  release pull request on every push. The risk is recorded here rather than
  mitigated: if `release-please.yml` starts failing with that 422, this is why.
- A failed check on the package release pull request stops the automation with a
  non-zero exit rather than waiting. That is deliberate: only a person can fix a
  failing check, and waiting would hide it until the timeout.

## Reversal condition

Reopen this decision if release-please gains the ability to release several
components from one pull request in a defined order, or if the dispatcher build
becomes reproducible so the pin's asset digests can be computed before the
release exists. In either case the components can share a release pull request
again and the wait-and-merge step becomes unnecessary. Finding three release
pull requests noisier than one is not a reversal condition.
