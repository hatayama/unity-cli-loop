# Regression harness for verification traps

Manual verification rounds against `uloop` (pause-point, simulate-keyboard,
physics callbacks, etc.) repeatedly rediscover the same handful of edge cases
("traps") because each round's repro lives only in a throwaway feedback memo.
This harness makes a trap's repro steps a permanent, re-runnable asset instead.

## Layout

- Scene + helper C# for a trap: `Assets/RegressionHarness/<TrapName>/`. Keep
  each scene minimal — just enough MonoBehaviour/GameObject setup to trigger
  the trap's condition, not a playable game. Do not mix these with
  `Assets/Editor/CustomCommandSamples/` (that folder is for first-party tool
  usage samples, not bug-repro scenes).
- uloop command sequence for a trap: `scripts/regression-harness-<trap-name>.sh`
  (POSIX sh, flat under `scripts/` to match this repo's existing script
  layout). The script drives the trap end to end — enable/arm, trigger the
  action, await the result — and compares the response against the expected
  outcome, exiting non-zero on mismatch.

## Execution

These scenarios require a running Unity Editor with the trap scene open, so
they are not part of automated CI. Run them manually (or via an agent) before
merging a PR that touches the pause-point or simulate-keyboard code paths the
scenario covers.

## Existing scenarios

| Trap | Scene | Script |
|------|-------|--------|
| Key state divergence after a pause-point interruption | `Assets/RegressionHarness/KeyStateAfterPauseInterruption/` | `scripts/regression-harness-key-state-after-pause-interruption.sh` |
| `await-pause-point --trigger` hits within the marker timeout instead of waiting out the triggered command's full duration | `Assets/RegressionHarness/KeyStateAfterPauseInterruption/` (reused) | `scripts/regression-harness-pause-point-trigger.sh` |
| A pause point armed on an OnCollisionEnter2D/OnTriggerEnter2D method misses a GameObject that already existed before arming, and toggling the component's `enabled` off/on resolves it | `Assets/RegressionHarness/PhysicsCallbackExistingInstance/` | `scripts/regression-harness-physics-callback-existing-instance.sh` |
