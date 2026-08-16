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
merging a PR that touches the pause-point, simulate-keyboard, or hot-reload
code paths the scenario covers.

## Existing scenarios

| Trap | Scene | Script |
|------|-------|--------|
| Key state after a pause-point interruption: (1) Press reports `InterruptedByPausePoint`, (2) resume within the apply-timeout window after interrupt must not re-press, (3) after a >6s pause `ReleaseAll` while still paused restores keys without clearing captures, then a fresh Press succeeds | `Assets/RegressionHarness/KeyStateAfterPauseInterruption/` | `scripts/regression-harness-key-state-after-pause-interruption.sh` |
| `await-pause-point --trigger` hits within the marker timeout instead of waiting out the triggered command's full duration | `Assets/RegressionHarness/KeyStateAfterPauseInterruption/` (reused) | `scripts/regression-harness-pause-point-trigger.sh` |
| `enable-pause-point --await --resume-play --trigger` resumes a manually paused PlayMode before firing the input trigger and hits within the marker timeout | `Assets/RegressionHarness/KeyStateAfterPauseInterruption/` (reused) | `scripts/regression-harness-resume-play-paused-arm.sh` |
| A pause point armed on a physics message method (or a method called one hop from one) can miss a GameObject that already existed before arming. The miss itself is environment-dependent and does not reproduce deterministically (see below) -- the harness runs three scenarios (direct/OnCollisionEnter2D, indirect callee with priming, and OnTriggerEnter2D), each triggering a fresh contact after arming and classifying the result from the component's own hit counter plus `IsHit` | `Assets/RegressionHarness/PhysicsCallbackExistingInstance/` | `scripts/regression-harness-physics-callback-existing-instance.sh` |
| Hot reload of an in-body string literal in `Update` changes PlayMode console output without a domain reload, then `--revert-all` restores the previous body. Sed targets a method-body literal only (a class-level `const` or field initializer is outside hot-reload scope and would silently leave behavior unchanged despite `Success`) | `Assets/RegressionHarness/HotReload/` | `scripts/regression-harness-hot-reload.sh` |
| Hot reload of an added field plus an added method in the same file: existing `Update` starts calling the new method, patched `WriteAdded` stores `10`, re-apply keeps `10`, `--status` lists Kind `Added`, and `--revert-all` restores the baseline marker. A newly added Unity message would not run (added messages are not discovered) | `Assets/RegressionHarness/HotReloadAddedMember/` | `scripts/regression-harness-hot-reload-added-member.sh` |
| Hot reload of a return-type change: same-file caller is patched and PlayMode logs the new value; a compiled caller in another file causes the changed method and its edited same-file caller to `Skipped`. `--revert-all` restores the baseline marker | `Assets/RegressionHarness/HotReloadSignatureChange/` | `scripts/regression-harness-hot-reload-signature-change.sh` |

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

#### 2026-07-22 field capture: direct-marker miss, and clear-then-re-enable recovers it

A Round-8 verification round captured the miss in full for the first time on a marker placed
directly on the callback body (not one hop away), which rules out the Mono JIT-inlining
hypothesis for this particular case -- there is no indirection for the JIT to inline through.

- `Block.cs:29` (`OnCollisionEnter2D`), `InstanceCount=39`: the marker missed 29 seconds of
  continued fresh contacts (score kept climbing on each collision, proving the callback itself
  kept running normally; only the pause-point marker failed to fire). Clearing the marker
  (`StatusBeforeClear=Enabled`, i.e. it had not expired) and re-enabling it on the same instance
  hit immediately on the very next fresh contact -- clear-then-re-enable ("re-arm") recovered the
  marker without any other change.
- `Ball.cs:70`, `InstanceCount=1`, for contrast: hit normally on the first fresh contact, no
  domain reload involved, same domain age (252s) as the miss above -- so domain age alone does
  not predict the miss.
- The expiry-only diagnostic of the time (`pause_point_expired_without_hit_physics`) did not
  fire for the `Block.cs:29` miss because the CLI-side `await-pause-point` timeout (25s) was
  shorter than the marker's own capture timeout (30s); the CLI gave up and cleared the marker
  while its status was still `Enabled`, not `Expired`. That observability gap is what this
  round's PR-1 closes: the diagnostic is now `pause_point_cleared_without_hit_physics` and fires
  on any zero-hit clear of a physics-flagged marker -- whether it had already expired or was
  still `Enabled` when cleared -- with `StatusBeforeClear` in the context to tell the two apart.

Re-arming (clear the marker, then `enable-pause-point` it again on the same instance) is now a
reasonable first thing to try when a physics-flagged marker times out despite continued fresh
contact, on top of the existing options above -- but treat it the same as those: environment-
dependent, observed once in the field so far (2026-07-22), and not a guaranteed fix.
