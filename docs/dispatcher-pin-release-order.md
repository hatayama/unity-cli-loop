# Dispatcher pin release order

The Unity package uses a provenance-pinned dispatcher Release for first
installation. Package release preparation must follow this order:

1. Publish the dispatcher Release and its Sigstore attestations.
2. Stamp the pin against that immutable Release. For a **stable** release the
   `dispatcher-publish` workflow does this on its own: after publishing, its
   `post-publish` job runs `open-dispatcher-pin-pr`, which branches from `main`
   as `chore/dispatcher-pin-<tag>`, stamps `Packages/src/project-runner-pin.json`,
   mirrors it byte-identically to `.uloop/project-runner-pin.json`, re-verifies
   the stamp offline and against the published release, and opens the pull
   request for a human to review and merge. It leaves `minimumDispatcherVersion`
   alone, is idempotent (a re-run with nothing to change opens no pull request),
   and never stamps a **pre-release** — those stay off `main`. `stamp-dispatcher-pin`
   remains the manual fallback when the automated pull request has to be redone by hand.
3. Verify the resulting pin with `check-dispatcher-pin` and publish the Unity
   package.

A pull request created with `GITHUB_TOKEN` does not trigger `pull_request`
workflows, so `open-dispatcher-pin-pr` dispatches the required check workflows
itself once the pull request exists.

Changing `scripts/install.sh` or `scripts/install.ps1` does not immediately
change the Unity first-install path. Unity downloads the script from the
Release named in its package pin. The change becomes active only after a new
dispatcher Release is published and a later package release stamps that
Release. The pin guard reports source-script drift for review but does not
block the dispatcher Release that is required to resolve it.
