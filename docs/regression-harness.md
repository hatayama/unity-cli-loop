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
| A pause point armed on a physics message method (or a method called one hop from one) can miss a GameObject that already existed before arming. The miss itself is environment-dependent and does not reproduce deterministically (see below) -- the harness runs three scenarios (direct/OnCollisionEnter2D, indirect callee with priming, and OnTriggerEnter2D), each triggering a fresh contact after arming and classifying the result from the component's own hit counter plus `IsHit` | `Assets/RegressionHarness/PhysicsCallbackExistingInstance/` | `scripts/regression-harness-physics-callback-existing-instance.sh` |

### Physics-callback existing-instance miss: environment-dependent, not deterministic

A "miss" is only valid evidence when a fresh contact is actually triggered after arming and the
component's own hit counter proves the method body ran; `IsHit=false` on its own only means no new
collision occurred in the check window, not that dispatch was missed. An earlier version of this
harness checked baseline `IsHit` without ever triggering a fresh contact after arming, which made
every "miss" it reported a false positive (the ball had already settled from a fall that happened
before arming) -- including the "enabled-toggle workaround fixes it" conclusion that briefly lived
in this doc and in `SourcePausePointConstants`. That conclusion has been retracted; the harness now
requires a counter increment alongside `IsHit=false` before it will call something a genuine miss.

The following were ruled out as the deterministic trigger condition through controlled
experiments (each patched correctly, i.e. did not reproduce the miss): a fresh domain reload
immediately before arming, priming the JIT before arming, arming right after a script compile,
arming in a freshly launched Editor process, arming in a freshly launched process right after a
compile, arming on a runtime-`AddComponent`-created instance, and arming a one-hop indirect
callee with the instance primed by one prior contact. The miss is real (it has been observed in
real projects), but no deterministic reproduction recipe is known, and no lighter workaround than
destroying and recreating the GameObject (or a manual `UloopPausePoint.Pause` marker) has been
validated -- see `SourcePausePointConstants.PhysicalCallbackMayMissExistingInstanceWarning`.
`PausePointTools` logs a `pause_point_physics_dispatch_diagnostics` VibeLogger entry whenever a
physics-flagged pause point is enabled (and a `pause_point_cleared_without_hit_physics` entry if
it is later cleared without ever hitting, whether it had expired or was still Enabled at that
point), capturing Play Mode state, seconds since the last domain reload, the declaring type, and
its current instance count -- if the miss recurs, this is the primary evidence to work from.
