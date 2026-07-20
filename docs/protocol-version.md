# CLI / Unity package protocol version

Runtime compatibility between the Unity package and the native CLI is gated on
an integer protocol version, not on release numbers. Two declarations must
always stay equal:

- Go side: `protocolVersion` in `cli/common/clicontract/contract.json` (the generation the CLI advertises over IPC).
- C# side: `CliConstants.REQUIRED_CLI_PROTOCOL_VERSION` (the exact generation the package accepts).

`TestProtocolVersionMatchesUnityPackage` fails the build if they diverge, so
never bump one alone. The runtime gate expects equality because the protocol
version is a contract generation, not a minimum-compatible range.

Pull request CI also runs a non-blocking IPC protocol reminder when IPC-facing
files changed without protocol declaration changes; treat it as a review
prompt, not as proof that a bump is required.

## When to bump

Bump both, together, in the same PR only when the IPC contract changes in a
way that makes CLI and package builds from different protocol generations
unable to interoperate — for example renaming or removing a request field,
changing the readiness/dispatch handshake, or altering a response shape the
other side parses. Ordinary CLI features and bug fixes that keep the wire
format compatible must not bump it.

## Release sequencing and mismatch guidance

Do not touch the protocol version to "keep up with releases":

- `cli/common/clicontract/contract.json` `projectRunnerVersion`, the pin files'
  `projectRunnerVersion`, and `cli/dispatcher/dispatchercontract/dispatcher-contract.json`
  `dispatcherVersion` are stamped by release-please only. Never edit them by hand in a feature PR
  (the one exception is a version series realignment; see `docs/version-series-realignment.md`).
- When a protocol bump changes `CliConstants.REQUIRED_CLI_PROTOCOL_VERSION`, prepare the matching
  project runner release first. PR CI (`check-protocol-minimum-version`) fails until the pin's
  `projectRunnerVersion` points at a published project runner release that advertises the
  required protocol; release-please advances that value when the runner release is cut.
- Runtime protocol mismatch guidance must use the unpinned CLI update path for older clients and
  tell newer clients to align the package and CLI releases.
