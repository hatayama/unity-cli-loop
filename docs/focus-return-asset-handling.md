# Focus-return handling of open Scenes and the Prefab Stage

The Unity package runs a preflight on `EditorApplication.focusChanged(true)`. The preflight
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
become saved, and `run-tests --unsaved-changes fail` had nothing left to detect. Saving is
now gated on an actual external change, which is the only case the dialog-avoidance rationale
covers.

## Why focus-return preflight does not hold Auto Refresh

Through 3.2.1 the package called `AssetDatabase.DisallowAutoRefresh` on focus loss and released
it only after the preflight ran. That hold is what caused issue #2575: Unity evaluates its own
focus-return Auto Refresh while the hold is still active, skips it, and does not reschedule it
after `AllowAutoRefresh`, so `.cs` files added or edited outside Unity stayed unimported until
an explicit `uloop compile`.

A/B checks on Unity 2022.3.62f3 and 6000.3.15f1 showed the same Editor order every time:
native refresh (import) → C# `EditorApplication.focusChanged(true)` (preflight) → Unity's
scene-change check (the tick that would raise the dialog). With that order the preflight
alone prevents the native dialog; a focus-loss hold did not contribute.

Do not reintroduce a hold for the focus-return preflight unless a Unity version is found
where the native dialog appears before the preflight runs. Even then, prefer a
version-specific workaround over holding Auto Refresh for every Editor.

A different hold exists for live hot-reload patches. `HotReloadAutoRefreshHold` calls
`DisallowAutoRefresh` only while `HotReloadPatcher.ActiveChangeCount > 0`, so a
`uloop focus-window` cannot import edited `.cs` files and stop Play Mode. The hold is
released when the ledger returns to 0 (revert-all, a run that clears the last patch,
compile, or domain reload). Release then calls `AssetDatabase.Refresh` when the Editor is
focused and not playing; during Play the refresh is deferred to the next focus return or
`uloop compile`. Explicit `AssetDatabase.Refresh` from `uloop compile` still runs while
the hold is active.

## Related but separate paths

- `uloop compile` runs the same fingerprint comparison before compiling (`ExternalSceneChangeResolver`).
  There, a dirty Scene that changed externally is reported and never overwritten; clean changed
  Scenes are reloaded, and dirty Scenes are saved first only when a reload of the whole Scene
  setup is required. `--stop-on-external-scene-changes` turns the reload into a hard stop.
  The compile preflight imports those changed Scene assets synchronously before reloading them.
  Reloading first leaves the loaded Scene tied to the stale import, so the following
  `AssetDatabase.Refresh` raises Unity's "modified externally" dialog.
- `uloop run-tests` and `uloop control-play-mode` save unsaved Scene and Prefab Stage changes
  before starting by default; that is an explicit, documented step in those tools, not part of
  focus return.
