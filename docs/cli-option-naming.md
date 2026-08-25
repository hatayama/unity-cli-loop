# CLI Option Naming

Rules for naming first-party tool options (schema property names and their
kebab-case CLI flags). Apply them when adding an option or auditing an
existing one. Renaming a published option is a breaking change — schema
property names are JSON-RPC Params keys — and requires a protocol version
bump; see `docs/protocol-version.md`.

- Result-row caps use `Max<Noun>Count`, or plain `MaxCount` (`--max-count`)
  when the tool has a single primary result list. A result-row cap limits how
  many rows of that list the response returns.
- Timeouts use `TimeoutSeconds` (`--timeout-seconds`), with the unit in the
  name. Tool-specific prefixes (such as the former
  `CompileWaitTimeoutSeconds`) are not used: within one tool the bare name is
  unambiguous.
- File targets use `OutputPath`; directory targets use `OutputDirectory`.
  The two are distinct on purpose — do not unify them.
- Caps that bound something other than result rows keep their domain names:
  `MaxHistory` (retained history entries), `MaxPreviewElements` (preview
  depth), `MaxCallerFrames` (captured frames). Renaming them to `Max*Count`
  would erase what is being bounded.

Renamed in the alignment pass, with no compatibility aliases:
`CompileWaitTimeoutSeconds` → `TimeoutSeconds` (compile) and `MaxResults` →
`MaxCount` (find-game-objects). The old flags fail with `INVALID_ARGUMENT`,
and the unknown-option suggestion proposes the new name.
