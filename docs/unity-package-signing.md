# Unity Package Signing

Unity 6.3 and later check a signature on every tarball package and warn in the Package Manager
window when a package has none. OpenUPM does not sign packages: with
`trackingMode: githubRelease` it republishes, unchanged, a tarball attached to the GitHub
release of each version tag. The `unity-package-sign` workflow produces that tarball.

## Flow

1. `release-please.yml` creates the `v<version>` release with `GITHUB_TOKEN`. That starts no
   release or tag-push workflow, so `unity-package-sign.yml` runs on `workflow_run` after every
   `release-please` run on `main` instead.
2. The `plan` job runs `cli/release-automation/cmd/plan-unity-package-signing` on the commit the
   triggering `release-please` run processed, not the newest `main` commit, so a release commit
   that lands right after another cannot hide the earlier release. It signs only a published
   release that has no non-empty `io.github.hatayama.uloopmcp-<version>.tgz` asset, so most runs
   end here. A manual dry run signs any published release.
3. The `sign` job checks out the release tag, installs the pinned UPM CLI, runs `upm pack`, and
   runs `cmd/stage-signed-unity-package`. Staging rejects a tarball without
   `package/.attestation.p7m`, with a different name or version, or missing any file of the
   tag's `Packages/src` tree (a dropped asset `.meta` file or `Editor/CliOnlyTools~/` skill), and
   renames it to the asset name.
4. The `publish` job attaches the tarball to the release. It is the only job with
   `contents: write`, and it never runs the UPM CLI. The `sign` job, which holds the signing
   credentials, cannot write to the repository.

## Components

| Piece | Role |
| --- | --- |
| `upm-signing` environment | Holds the three signing secrets. Its deployment branch policy allows `main` only, so a manual run on another branch, whose workflow file could be edited, gets no secrets. Keep the secrets out of repository-level secrets, which every branch can read. |
| `UPM_SERVICE_ACCOUNT_KEY_ID`, `UPM_SERVICE_ACCOUNT_KEY_SECRET` environment secrets | Key pair of a Unity Cloud service account with the organization role **Package Manager Package Signer**. Read only by the `Sign the package` step. |
| `UPM_ORG_ID` environment secret | ID of the Unity organization that signs the package (Unity Cloud Dashboard → Administration → Settings). |
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
   release tag. A dry run signs a release even when it already carries the tarball.

## Operations

- **Manual run.** Run it on `main`; other branches get no signing secrets. `workflow_dispatch`
  takes a `tag` (default: the manifest version) and `dry-run` (default: on). A dry run signs any
  published release, keeps the tarball as the `signed-unity-package` workflow artifact for 14
  days, and attaches nothing. Turn `dry-run` off to sign a release whose automatic run failed.
- **Failure.** A failed run opens or updates the Release Failure Notify issue. OpenUPM keeps
  polling a release without the asset for up to three days. After that, attach the tarball with
  a manual run and request a rebuild on OpenUPM.
- **Check a release.** `curl -s https://api.openupm.com/packages/io.github.hatayama.uloopmcp`
  reports `"source": "githubRelease"` and `"signed": true` for a version OpenUPM published from
  the signed tarball. In the Unity Package Manager window, the warning only reports correctly on
  Unity 6000.3.5f2 and later; earlier 6.3 builds can call a valid signature invalid.
- Versions published before the switch stay unsigned. OpenUPM does not republish an existing
  version.
