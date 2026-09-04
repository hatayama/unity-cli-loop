# Dispatcher Pin Direct Push

Date: 2026-09-05

## Decision

After a **stable** dispatcher Release is published, the `dispatcher-publish`
workflow stamps `Packages/src/project-runner-pin.json` (and its `.uloop/` mirror)
and pushes the stamp commit straight to `main`, using a GitHub App token that the
`main` branch ruleset lists as a bypass actor. No pull request is opened and no
human merges the stamp.

`push-dispatcher-pin` replaces `open-dispatcher-pin-pr`. The pin content, the
verification it undergoes, and the release order in
`docs/dispatcher-pin-release-order.md` are unchanged; only the transport from
"verified stamp" to "commit on `main`" changed.

## Context

The stamp records the release tag and the attestation-verified asset digests of a
dispatcher Release that already exists. Everything a reviewer could check is
checked by machine before the commit is created: `StampDispatcherPin` verifies
the attestations while writing the manifest, `ValidateDispatcherPinOffline`
checks the shape and the mirror, `VerifyDispatcherPinSubjects` compares the
manifest with the published release subjects, and `check-dispatcher-pin` repeats
the check on `main`. A value that does not match the published release cannot
pass any of these, whoever wrote it; the checks validate content, not authorship.

The only human decision in a dispatcher release is whether to publish it, and
that decision is taken at the `cli-release` environment approval before the
Release exists. The pin pull request asked the same person to approve the same
release a second time, with nothing new to look at. In practice it was merged
without being read.

The pull request also produced noise on every release: a pull request authored
by `github-actions[bot]` triggers `pull_request` workflows that immediately stop
in `action_required`, waiting for workflow approval that nobody grants because
the automation dispatches the same checks itself via `workflow_dispatch`. Four
"Action required" runs per release accumulated in the Actions list.

## Alternatives considered

- **Auto-merge the pull request once its dispatched checks pass**: keeps the
  pull request as a record but still produces the `action_required` runs, still
  needs a token that can merge past the ruleset, and adds a polling loop for
  checks that already ran on the same content. More machinery for no additional
  guarantee.
- **Fold the stamp into the release-please release pull request**: no new
  credentials, but the stamp then depends on a release pull request being open
  at the moment the dispatcher publishes, and release-please rewrites that
  branch on every run, so the stamp would have to be re-applied after each
  rewrite. It also couples two independent release flows.
- **Grant `GITHUB_TOKEN` a ruleset bypass**: not possible; GitHub Actions
  workflow tokens are not selectable as bypass actors. A GitHub App is the
  narrowest identity that is.
- **Use a personal access token of an administrator**: a person's token
  authoring release commits ties automation to one account and grants far more
  than Contents write on one repository.

## Consequences

- One GitHub App, one repository variable (`DISPATCHER_PIN_APP_ID`), one secret
  (`DISPATCHER_PIN_APP_PRIVATE_KEY`), and one ruleset bypass entry are new
  operational state. Setup steps: `docs/dispatcher-pin-release-order.md`.
- The pin reaches `main` minutes after the dispatcher Release, without waiting
  for a person. `scripts/install.sh` and `scripts/install.ps1` read the pin from
  `main` directly, so terminal installs follow the new dispatcher immediately;
  Unity's Install CLI button follows at the next package release, exactly as
  before.
- `check-dispatcher-pin-freshness` remains the detector for a stamp that failed
  to land (for example a rejected push because `main` moved), and its message
  now points at re-running the `post-publish` job rather than merging a pull
  request.
- The `post-publish` job no longer needs `pull-requests: write` or
  `actions: write`.

## Reversal condition

Reopen this decision only if the `cli-release` environment approval is removed
from `dispatcher-publish`, so that no human gate remains anywhere between "a
commit landed on `main`" and "new installs receive that dispatcher" — or if the
pin ever starts carrying a value that automation cannot verify (for example a
manually chosen `minimumDispatcherVersion` bump). Wanting a pull request purely
as a visible record is not a reversal condition; the stamp commit on `main` and
the release workflow run are that record.
