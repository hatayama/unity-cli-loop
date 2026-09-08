# Dispatcher pin release order

The Unity package uses a provenance-pinned dispatcher Release for first
installation. Package release preparation must follow this order:

1. Publish the dispatcher Release and its Sigstore attestations.
2. Stamp the pin against that immutable Release. For a **stable** release the
   `dispatcher-publish` workflow does this on its own: after publishing, its
   `post-publish` job runs `push-dispatcher-pin`, which checks out the tip of
   `main`, stamps `Packages/src/project-runner-pin.json`, mirrors it
   byte-identically to `.uloop/project-runner-pin.json`, re-verifies the stamp
   offline and against the published release, and pushes the resulting commit
   straight to `main`. It leaves `minimumDispatcherVersion` alone, is idempotent
   (a re-run with nothing to change pushes nothing), and never stamps a
   **pre-release** — those stay off `main`. `stamp-dispatcher-pin` remains the
   manual fallback when the automated push has to be redone by hand.
3. Verify the resulting pin with `check-dispatcher-pin` and publish the Unity
   package.

`dispatcher-pin-freshness.yml` runs `check-dispatcher-pin-freshness` on every
push to `main`, once a day on a schedule, and on manual dispatch, so `main` fails
while the pin still records an older release than the newest published stable
`dispatcher-v*` one. The failure clears by re-running the `post-publish` job of
the `dispatcher-publish` run that published the release, or by stamping by hand
with `stamp-dispatcher-pin --tag <tag>`. The gate deliberately does not run on
`pull_request`: until the stamp lands, every other branch carries the same
lagging pin, and failing there would only hide the state that `main` alone can
fix. The schedule exists because a dispatcher release can be published without
any later push to `main`.

## Release pull requests

release-please opens **one release pull request per component**: `unity-package`,
`dispatcher`, and `uloop-project-runner`. The **unity-package** one stays draft
and is merged only by automation; the other two are the ones people merge.

1. Merge the **dispatcher** release pull request and approve the `cli-release`
   environment. Publishing the dispatcher is the only human decision.
2. `post-publish` stamps the pin on `main`, then marks the **unity-package**
   release pull request ready and merges it, once that pull request's head
   records the dispatcher tag just published and has a successful run of every
   required workflow for that exact head commit.
3. A package release that carries no dispatcher bump never reaches step 2, so
   `release-please.yml` merges it instead: after the release PR checks, it
   marks the package pull request ready and merges it, but only while no
   dispatcher release pull request is open **and** the pin at the package head
   already records the dispatcher version `main`'s manifest releases. Otherwise
   it leaves the pull request draft and reconsiders on the next push to `main`.
4. The **project runner** release pull request carries no cross-component
   ordering constraint and can be merged at any time.

Merging the package pull request before the dispatcher one would ship the
*previous* dispatcher, which is the lag this order exists to remove, so it is
not offered: the pull request is left in draft and the merge button is not
available. Doing it anyway takes two deliberate actions — "Ready for review",
then merge — rather than one misplaced click.

Both automated paths reason about the pin at the pull request head, never about
the draft flag. "No dispatcher release pull request is open" alone does not mean
the stamp landed: the pull request disappears from the open list the moment it
merges, while the stamp arrives 15–25 minutes later. That window is why the
`release-please.yml` path also requires the head pin to match `main`'s manifest.
The `post-publish` path deliberately does *not* require the absence of an open
dispatcher pull request, because the next cycle's dispatcher release can already
be pending by the time the stamp lands.

The config sets `always-update: true` alongside `separate-pull-requests`. Without
it release-please pushes a release branch only when the pull request body
changes, and the pin stamp is a `chore` commit that appears in no changelog: the
unity-package release pull request would never move onto the stamp, so its head
would keep the old pin and the automatic merge would time out. It would also
keep the `.release-please-manifest.json` conflict that merging the dispatcher
release pull request creates, since both components' entries sit on adjacent
lines. The cost is that every push to `main` rebases each pending release pull
request and re-dispatches its checks.

`check-package-pin-consistency` enforces the rule rather than trusting it. It
runs as the `check-package-release-pin` job on the unity-package release branch,
and again inside `sync-release-please-package-releases.sh` before the package
release is created or published; a release commit whose pin and manifest name
different dispatchers fails instead of releasing. The job is scoped to that one
branch on purpose: between the dispatcher merge and the stamp, `main`'s manifest
and pin legitimately disagree, and gating every pull request would turn that
window red for unrelated work.

When the automatic merge does not happen, merging the unity-package release pull
request by hand means re-checking by hand what the automation would have
checked. A green pin freshness gate on `main` only proves the stamp landed; it
says nothing about the pull request. Before merging, confirm all four:

- the pin freshness gate on `main` is green, so the stamp reached `main`;
- the pull request's head commit records the dispatcher tag just published in
  `Packages/src/project-runner-pin.json`;
- no dispatcher release pull request is open, or the one that is open belongs to
  the *next* cycle and the head pin already records the dispatcher `main`
  releases;
- every required workflow has a completed successful run for that exact head
  SHA — not for an earlier head.

Merging without those is how a stale or unvalidated package release gets
published, which is the failure this whole order exists to prevent. Decision
record: `docs/adr/0007-separate-release-prs-and-package-auto-merge.md`.

Once all four hold, take the pull request out of draft yourself ("Ready for
review") and merge it. A **pre-release** dispatcher is never stamped, so a
package release waiting on one stays draft indefinitely: publish a stable
dispatcher, or merge by hand as above.

When the stamp itself fails, the package pull request simply stays draft. Re-run
the `post-publish` job of the `dispatcher-publish` run: it stamps the new tip and
merges the pull request in the same job.

## Why the stamp is pushed without a pull request

Until 2026-09 the stamp travelled as an automated pull request that a human
merged. The merge added no information: the pin content is fully
machine-verified (attestation check while stamping, offline validation, and a
re-verification against the published release subjects), `check-dispatcher-pin`
verifies it again on `main`, and the only human decision in the dispatcher
release — whether to publish at all — is already taken at the `cli-release`
environment approval. The pull request was merged unread in practice, and it
also left four `action_required` `pull_request` runs behind on every release
because bot-authored pull requests wait for workflow approval. Decision record:
`docs/adr/0005-dispatcher-pin-direct-push.md`.

## Push credentials

The `main` ruleset requires pull requests, and `GITHUB_TOKEN` cannot bypass a
ruleset. The push therefore uses a GitHub App token minted in the `post-publish`
job with `actions/create-github-app-token`. Repository setup, done once by an
administrator:

1. Create a GitHub App owned by the repository owner with the **Contents:
   Read and write** and **Pull requests: Read and write** repository permissions
   and no others. Install it on this repository only. Pull requests write is
   what lets `post-publish` merge the unity-package release pull request after
   the stamp; without it that step fails with a 403 and the pull request has to
   be merged by hand, under the conditions listed in "Release pull requests".
2. Store the App ID as the repository variable `DISPATCHER_PIN_APP_ID` and a
   generated private key as the repository secret
   `DISPATCHER_PIN_APP_PRIVATE_KEY`.
3. Add the App as a bypass actor of the `default` branch ruleset, with bypass
   mode **Always**. Do not add any user or role as a bypass actor; the App is the
   only identity allowed to push to `main` without a pull request.

The push is a plain fast-forward push, never a force push. If `main` moves
between the fetch and the push, the push is rejected and the job fails; re-run
the `post-publish` job and it stamps the new tip. The stamp commit is authored
as `github-actions[bot]`, so `main` history shows it as automation regardless of
which App pushed it.

Changing `scripts/install.sh` or `scripts/install.ps1` does not immediately
change the Unity first-install path. Unity downloads the script from the
Release named in its package pin. The change becomes active only after a new
dispatcher Release is published and a later package release stamps that
Release. The pin guard reports source-script drift for review but does not
block the dispatcher Release that is required to resolve it.
