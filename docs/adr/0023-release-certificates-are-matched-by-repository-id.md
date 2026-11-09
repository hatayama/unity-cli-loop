# Release Certificates Are Matched by Repository ID, Not Repository Name

Date: 2026-11-09

## Decision

The dispatcher accepts a release asset only when its Sigstore certificate carries this
repository's numeric GitHub repository ID (Fulcio OID `1.3.6.1.4.1.57264.1.15`) and owner ID
(`…1.17`), the release tag's commit (`…1.13`), the token issuer, and a SAN whose workflow path
and ref are on the allowlist (`cli/dispatcher/attestation/policy.go`). The SAN is matched with
an anchored pattern whose owner and repository name segments accept any name; the source
repository URI (`…1.12`) is not compared.

`ReleaseRepository` (the `owner/name` string) stays, but only to build GitHub API and download
URLs. It plays no part in deciding whether a release is trusted.

## Context

- The certificate's repository name is whatever the repository was called when the release was
  signed. Matching on it would make every release signed after a rename fail in every
  dispatcher already installed, and a dispatcher in that state cannot heal itself: its
  self-update resolves the newest stable release and verifies it against the same name.
- GitHub keeps a repository's numeric ID through a rename (the 2026-03-16 rename from
  `uLoopMCP` kept ID `1002447118`), and a repository deleted and recreated under the same name
  gets a new one, so the ID binds a certificate to this repository more tightly than the name.
- On 2026-11-09 the certificates of all 69 published dispatcher and project runner releases
  (dispatcher-publish.yml and native-cli-publish.yml, refs `main` and `v3-beta`) carried
  repository ID `1002447118` and owner ID `842587`, so matching on them accepts every existing
  release.
- sigstore-go (`certificate.CompareExtensions`) skips extension fields left empty. An identity
  with an empty ID would accept any repository, so `Verify` and `VerifySubjects` reject an
  identity missing either ID before they reach the matcher.

## Rejected Alternatives

- **Accept a list of repository names (old and new).** Every future rename would need another
  dispatcher release, published and adopted before the rename, and the list would keep trusting
  names the repository no longer uses.
- **Drop the repository check and keep only the workflow path and ref.** Anyone can obtain a
  valid Fulcio certificate for a workflow at the same path in their own repository; the check
  would then prove only that someone signed the asset.

## Consequences

- The SAN's owner and repository segments are no longer pinned. A certificate passes only if
  the workflow ran in this repository (the IDs), so the only extra case accepted is a workflow
  file of this repository calling a reusable workflow of the same path in another repository —
  which only someone who can already change this repository's workflows can arrange.
- Dispatchers released before this decision still match on `hatayama/unity-cli-loop`. The
  repository can be renamed once a dispatcher that matches by ID is the newest stable release,
  but the first dispatcher release signed after the rename should wait until older dispatchers
  have updated: their forced update resolves the newest stable release and can verify only one
  signed before the rename.

## Revisit When

- (a) Fulcio stops writing the repository or owner ID extensions for GitHub Actions
  certificates.
- (b) Fulcio adds an ID for the repository that owns the signing workflow, so the SAN side can
  be pinned by ID as well.
- (c) The repository moves to an owner whose ID differs from `842587`: update
  `ReleaseRepositoryOwnerID` in a dispatcher release before the transfer.
