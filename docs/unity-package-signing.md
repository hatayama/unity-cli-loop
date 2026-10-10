# Unity Package Signing

Unity 6.3 and later check a signature on every tarball package and warn in the Package Manager
window when a package has none. OpenUPM does not sign packages: with
`trackingMode: githubRelease` it republishes, unchanged, a tarball attached to the GitHub
release of each version tag. The `unity-package-sign` workflow produces that tarball.

This repository has immutable releases enabled: once a release is published, no asset can be
added to it (`HTTP 422: Cannot upload assets to an immutable release.`). The package release is
therefore created as a draft, and only `unity-package-sign.yml` publishes it, after the signed
tarball is attached.

## Flow

1. `scripts/sync-release-please-package-releases.sh` creates the `v<version>` release as a draft
   at the release commit, after its protocol and pin checks, and never publishes it. The sync
   runs in `release-please.yml`, `dispatcher-publish.yml`, and `native-cli-publish.yml` with
   `GITHUB_TOKEN`, which starts no release workflow, so `unity-package-sign.yml` runs on
   `workflow_run` after every run of those three workflows on `main`.
2. The `plan` job runs `cli/release-automation/cmd/plan-unity-package-signing` on the commit the
   triggering run processed, not the newest `main` commit, so a release commit that lands right
   after another cannot hide the earlier draft. It signs and publishes every draft. It never
   changes a published release: one that carries `io.github.hatayama.uloopmcp-<version>.tgz`
   needs nothing, and one without it gets a warning, because no run can attach the tarball any
   more. Most runs end here.
3. The `sign` job checks out the draft's target commit (a draft has no tag yet), installs the
   pinned UPM CLI, runs `upm pack`, and runs `cmd/stage-signed-unity-package`. Staging rejects a
   tarball without `package/.attestation.p7m`, with a different name or version, or missing any
   file of the release commit's `Packages/src` tree (a dropped asset `.meta` file or
   `Editor/CliOnlyTools~/` skill), and renames it to the asset name.
4. The `publish` job attaches the tarball to the draft and then publishes it, which creates the
   tag at the draft's target commit. It is the only job with `contents: write`, and it never runs
   the UPM CLI. The `sign` job, which holds the signing credentials, cannot write to the
   repository.

## Components

| Piece | Role |
| --- | --- |
| `upm-signing` environment | Holds the three signing secrets. Its deployment branch policy allows `main` only, so a manual run on another branch, whose workflow file could be edited, gets no secrets. Keep the secrets out of repository-level secrets, which every branch can read. |
| `UPM_SERVICE_ACCOUNT_KEY_ID`, `UPM_SERVICE_ACCOUNT_KEY_SECRET` environment secrets | Key pair of a Unity Cloud service account with the organization role **Package Manager Package Signer**. The role must be an organization role; a project role does not allow signing, and `upm pack` then fails with "User does not have permission to sign package". Read only by the `Sign the package` step. |
| `UPM_ORG_ID` environment secret | ID of the Unity organization that signs the package (Unity Cloud Dashboard → Administration → Settings). The Package Manager window shows the organization's name as "Signed for <name>". |
| `UPM_CLI_VERSION`, `UPM_CLI_LINUX_X64_SHA256` in `unity-package-sign.yml` | The pinned UPM CLI and the checksum recorded for its `upm-linux-x64.zip`. |
| `openupm/openupm` `data/packages/io.github.hatayama.uloopmcp.yml` | OpenUPM metadata. Needs `trackingMode: githubRelease` and `githubReleaseAssetName: 'io.github.hatayama.uloopmcp-'`; the asset name keeps OpenUPM away from the CLI archives on the dispatcher and project runner releases. |

## Updating the UPM CLI pin

The checksum is recorded in the workflow rather than read from the `.sha256` file beside the zip,
because both come from the same CDN and a replaced zip would come with a matching checksum.

1. Pick a release that has been public for a few weeks:
   `curl -fsSI https://cdn.packages.unity.com/upm-cli/releases/<version>/upm-linux-x64.zip`
   shows its `Last-Modified` date.
2. Before merging, download the zip, compute its SHA-256 yourself, and confirm it equals the
   published `upm-linux-x64.zip.sha256`. The workflow cannot test the pin before merging: the
   signing secrets are available only to runs on `main`.
3. Update `UPM_CLI_VERSION` and `UPM_CLI_LINUX_X64_SHA256` together.
4. After merging, run the workflow manually on `main` with `dry-run` checked and the latest
   release tag. A dry run signs a published release from its tag.

## Operations

- **Manual run.** Run it on `main`; other branches get no signing secrets. `workflow_dispatch`
  takes a `tag` (default: the manifest version) and `dry-run` (default: on). A dry run signs any
  existing release, draft or published, keeps the tarball as the `signed-unity-package` workflow
  artifact for 14 days, and neither attaches it nor publishes the release. Turn `dry-run` off to
  sign and publish a draft whose automatic run failed.
- **Failure.** A failed run opens or updates the Release Failure Notify issue and leaves the
  release a draft: there is no tag, OpenUPM does not see the version, and the merged release pull
  request keeps `autorelease: pending`, which stops release-please from opening release pull
  requests. Fix the cause, then run the workflow manually with `dry-run` off.
- **Never publish the package draft by hand.** Publishing it from the GitHub UI or with
  `gh release edit --draft=false` makes it immutable without the tarball, and that version can
  then never be signed. The plan reports such a release with a warning.
- **After publishing.** The release pull request label switches to `autorelease: tagged` on the
  next `release-please` run, not when this workflow publishes.
- **Check a release.** `curl -s https://api.openupm.com/packages/io.github.hatayama.uloopmcp`
  reports `"source": "githubRelease"` and `"signed": true` for a version OpenUPM published from
  the signed tarball. In the Unity Package Manager window, the warning only reports correctly on
  Unity 6000.3.5f2 and later; earlier 6.3 builds can call a valid signature invalid.
- Versions published before the switch stay unsigned. They are immutable without the tarball,
  and OpenUPM does not republish an existing version.
