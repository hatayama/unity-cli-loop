# Focus-return handling of open Scenes and the Prefab Stage

The Unity package holds Unity's Auto Refresh (`AssetDatabase.DisallowAutoRefresh`) while the
Editor is unfocused and releases it only after a preflight runs on focus return. The preflight
exists so that files replaced outside Unity while it was unfocused — a `git checkout`, a
revert, an agent editing a Scene file — do not surface as native "changed on disk" dialogs
that block `uloop` commands. Implementation: `ExternalSceneChangeTracker.ResolveForFocusReturn`
and `ExternalPrefabStageChangeTracker` under `Packages/src/Editor/FirstPartyTools/Compile/`.

## What the preflight does, per open asset

The package keeps a file fingerprint (`Exists`, `LastWriteTimeUtc`, `Length`) for every open
Scene and for the current Prefab Stage, recorded when the asset is opened or saved. On focus
return each asset falls into exactly one of these cases:

| In-memory state | File on disk vs. fingerprint | Action |
|---|---|---|
| clean | unchanged | nothing |
| clean | changed | reload the Scene setup / reopen the Prefab Stage from disk |
| clean | missing | write the asset back from memory |
| dirty | unchanged | **nothing — unsaved edits stay unsaved** |
| dirty | changed or missing | save the in-memory state over the file, then continue |
| any | no fingerprint recorded | record one, nothing else |

The `dirty + changed` row is the only place where the package writes a user's unsaved edits
to disk on its own, with one multi-Scene exception: when a *clean* open Scene changed on disk,
the reload goes through `EditorSceneManager.RestoreSceneManagerSetup`, which reloads every
open Scene, so the resolver saves all dirty Scenes first rather than lose their edits. That
still requires an external change to some open Scene; with nothing changed on disk, nothing
is saved. It does so because the alternative is a modal conflict dialog, and the
in-memory state is treated as authoritative there. The decision is isolated in
`ExternalAssetFocusReturnSavePolicy` so it can be unit-tested without Unity Scene APIs
(`Assets/Tests/Editor/ExternalAssetFocusReturnSavePolicyTests.cs`).

## Why dirty-but-unchanged assets are never saved

Through package 3.0.0 the preflight saved every dirty Scene and the dirty Prefab Stage on every focus
return, regardless of whether anything changed on disk. Because an agent-driven workflow
switches focus between the terminal and Unity constantly, that silently committed in-progress
editor work to disk within seconds of making it — the user saw a Scene they had not saved
become saved, and `run-tests --fail-on-unsaved-changes` had nothing left to detect. Saving is
now gated on an actual external change, which is the only case the dialog-avoidance rationale
covers.

## Related but separate paths

- `uloop compile` runs the same fingerprint comparison before compiling (`ExternalSceneChangeResolver`).
  There, a dirty Scene that changed externally is reported and never overwritten; clean changed
  Scenes are reloaded, and dirty Scenes are saved first only when a reload of the whole Scene
  setup is required. `--stop-on-external-scene-changes` turns the reload into a hard stop.
- `uloop run-tests` and `uloop control-play-mode` save unsaved Scene and Prefab Stage changes
  before starting by default; that is an explicit, documented step in those tools, not part of
  focus return.
