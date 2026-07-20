# Project runner pin

`Packages/src/project-runner-pin.json` (mirrored byte-identically to
`.uloop/project-runner-pin.json` by `CliPinSynchronizer`) is the single source
for cross-component version requirements. Its required fields:

- `projectRunnerVersion` — the project runner release the dispatcher must run for this package.
  Stamped by release-please; never edit by hand.
- `minimumDispatcherVersion` — the semver floor the package requires of the globally installed
  dispatcher. The dispatcher force-updates itself when it is older than this value, and the
  package reads it (via `CliPinReader`) for setup and installation checks. This is the only
  manually maintained minimum-version declaration; raise it only when the package genuinely
  needs a newly published dispatcher, not because the dispatcher implementation changed.
- `dispatcherReleaseTag` and `dispatcherArchiveManifest` — the provenance-pinned dispatcher
  release used for first installation and its verified asset hashes. Stamped by automation
  against a published release; never edit by hand — `VerifyDispatcherPinSubjects` requires the
  manifest to match the published release's verified subjects exactly, so a hand-written value
  cannot pass CI (see `docs/dispatcher-pin-release-order.md`).

There is no dispatcher⇄package integer contract generation; the pin's semver
floor is the only dispatcher gate. The IPC `protocolVersion` pair (see
`docs/protocol-version.md`) is the only integer generation in the system.

## Pin format discipline

The pin evolves additively only — never delete or rename an existing field.
The forced-update instruction (`minimumDispatcherVersion`) travels inside the
pin, so an old dispatcher that cannot parse a new pin never learns it must
update. For the same reason the dispatcher must stay lenient when reading pins
written by older packages.
