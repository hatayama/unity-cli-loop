# Unity Package Release

Unity 6.3 and later check a signature on every tarball package and warn in the Package Manager
window when a package has none. OpenUPM does not sign packages: with
`trackingMode: githubRelease` it republishes, unchanged, a tarball attached to the GitHub
release of each version tag.

This repository has immutable releases enabled: once a release is published, no asset can be
added to it (`HTTP 422: Cannot upload assets to an immutable release.`). The signed tarball
must therefore be part of the release when it is created. `unity-package-release.yml` is the
only workflow that creates the Unity package release (`v<version>`), and it creates it only
after the tarball is signed. The decision and the alternatives it replaced are recorded in
`docs/adr/0013-create-the-unity-package-release-after-signing.md`.

## Flow

1. **Trigger.** The workflow runs on `workflow_run` after every run of `release-please`,
   `dispatcher-publish`, and `native-cli-publish` on `main` that was not cancelled: merging the
   package release pull request, publishing the project runner, and publishing the dispatcher
   are the three things a package release waits for. Only `publish` is serialized across runs,
   so a run that skips or only signs never takes the place of a waiting creation; a waiting
   creation may be replaced by a newer one, which creates the same release or finds it published.
2. **`plan`** (`contents: read`) runs `cli/release-automation/cmd/plan-unity-package-release`
   on the newest `main` commit. While the package release pull request is still
   `autorelease: pending`, release-please opens no next release pull request, so the manifest
   there still names the release that is not out yet.
   - A published `v<version>` release is never changed. One that carries
     `io.github.hatayama.uloopmcp-<version>.tgz` needs nothing; one without it gets a warning,
     because no run can attach the tarball any more. Most runs end here.
   - Otherwise the plan runs `scripts/check-unity-package-release.sh`. It requires the
     project runner release of the manifest and the dispatcher release of the pin's
     `minimumDispatcherVersion` to be published with every asset, verifies the runner's
     attestations, finds the release-please release commit of the package version, and runs
     the protocol and pin checks at that commit. It prints that commit when the release is due,
     and nothing when a release it needs is not published yet; the plan then ends with a notice
     that names what it waits for, and the publish workflow of that release starts this
     workflow again. A failed check, a failed lookup, or an unreadable manifest, config, or pin
     fails the run.
3. **`sign`** (environment `upm-signing`, `contents: read`) checks out the release commit, or
   the tag of a published release in a dry run, installs the pinned UPM CLI, runs `upm pack`,
   and runs `cmd/stage-signed-unity-package`. Staging rejects a tarball without
   `package/.attestation.p7m`, with a different name or version, or missing any file of the
   release commit's `Packages/src` tree (a dropped asset `.meta` file or `Editor/CliOnlyTools~/`
   skill), and renames it to the asset name.
4. **`publish`** (`contents: write`) runs `cmd/create-unity-package-release`. Before writing
   anything it confirms that no release of the tag is published, that the tag, if it exists,
   points at the release commit, and that the changelog at the release commit has the
   version's section. It then deletes any draft of the tag a cut-off run left behind, creates
   the tag at the release commit if it is missing, and creates the release with the tarball
   in one `gh release create` call, which uploads to a draft and publishes it only after the
   upload succeeded. It finally reads the release back until it shows as published with a
   non-empty tarball. A pre-release version is published as a pre-release.
5. **`post-publish`** (`actions: write`) starts `release-please`. A release created with
   `GITHUB_TOKEN` starts no workflow, and it is release-please's label sync that marks the
   package release pull request `autorelease: tagged`, which lets release-please open the next
   release pull requests.

Neither `plan` nor `publish` runs the UPM CLI, and `sign`, which holds the signing credentials,
cannot write to the repository.

## Components

| Piece | Role |
| --- | --- |
| `upm-signing` environment | Holds the three signing secrets. Its deployment branch policy allows `main` only, so a manual run on another branch, whose workflow file could be edited, gets no secrets. Keep the secrets out of repository-level secrets, which every branch can read. |
| `UPM_SERVICE_ACCOUNT_KEY_ID`, `UPM_SERVICE_ACCOUNT_KEY_SECRET` environment secrets | Key pair of a Unity Cloud service account with the organization role **Package Manager Package Signer**. The role must be an organization role; a project role does not allow signing, and `upm pack` then fails with "User does not have permission to sign package". Read only by the `Sign the package` step. |
| `UPM_ORG_ID` environment secret | ID of the Unity organization that signs the package (Unity Cloud Dashboard → Administration → Settings). The Package Manager window shows the organization's name as "Signed for <name>". |
| `UPM_CLI_VERSION`, `UPM_CLI_LINUX_X64_SHA256` in `unity-package-release.yml` | The pinned UPM CLI and the checksum recorded for its `upm-linux-x64.zip`. |
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
4. After merging, run the workflow manually on `main` with `dry-run` checked. A dry run signs the
   published release of the manifest version from its tag.

## Operations

- **Manual run.** Run it on `main`; other branches get no signing secrets. `workflow_dispatch`
  takes only `dry-run` (default: on) and always works on the manifest version. A dry run signs
  the release commit of a release that is due, or the tag of a published release, keeps the
  tarball as the `signed-unity-package` workflow artifact for 14 days, and creates nothing. Turn
  `dry-run` off to create a release whose automatic run failed.
- **Failure.** A failed run opens or updates the Release Failure Notify issue. Until the release
  is created, the merged package release pull request keeps `autorelease: pending`, which stops
  release-please from opening release pull requests. Fix the cause, then run the workflow
  manually with `dry-run` off.
  - A failure before `publish` leaves nothing behind, and OpenUPM sees no new version.
  - A failure inside `publish` can leave the tag without a release. OpenUPM then holds the
    version, rechecking it at growing intervals for three days before it gives up on it, so
    create the release within that time. The next run reuses the tag.
  - A draft of the tag left by a `gh release create` that was cut off is deleted by the next
    run; do not publish it.
- **Never create or publish the package release by hand.** A release published without the
  tarball is immutable without it, and that version can then never be signed. The plan reports
  such a release with a warning on every run until the next version is released.
- **Check a release.** `curl -s https://api.openupm.com/packages/io.github.hatayama.uloopmcp`
  reports `"source": "githubRelease"` and `"signed": true` for a version OpenUPM published from
  the signed tarball. In the Unity Package Manager window, the warning only reports correctly on
  Unity 6000.3.5f2 and later; earlier 6.3 builds can call a valid signature invalid.
- Versions published before the switch stay unsigned. They are immutable without the tarball,
  and OpenUPM does not republish an existing version.
