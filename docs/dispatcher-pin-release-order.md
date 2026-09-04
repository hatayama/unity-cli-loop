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
   Read and write** repository permission and no other permissions. Install it
   on this repository only.
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
