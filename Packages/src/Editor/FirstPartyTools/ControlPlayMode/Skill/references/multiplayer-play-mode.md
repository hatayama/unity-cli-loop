# Multiplayer Play Mode scenarios

Read this when the project has a Play Mode Scenario (Multiplayer Play Mode) selected and you need
to start, stop, inspect, or drive its Virtual Players.

## Start and stop

- `uloop control-play-mode --action Play` starts the active scenario the same way the Editor's
  Play button does: Virtual Players are launched, then the main Editor enters Play Mode.
- `uloop control-play-mode --action Stop` stops the scenario, including its Virtual Players.
- `uloop control-play-mode --action Status` reports `ActiveScenario` with the name of the active
  Play Mode configuration. The field is absent while the default configuration is active.
- `Play` waits only until the main Editor is in Play Mode. It does not wait for every Virtual
  Player to finish starting; check each player's own state (below) when that matters.

## Find Virtual Players

Virtual Players are listed in `<PROJECT_ROOT>/Library/VP/SystemData.json`, under the `Data`
dictionary. For each entry, read:

- `Name`: the player name shown in the Multiplayer Play Mode window.
- `Active`: whether the player is enabled.
- `TypeDependentPlayerInfo.VirtualProjectIdentifier`: an object
  `{"m_Id": "<id>", "m_Prefix": "mppm"}`. It is null for the main Editor's own entry.

Concatenate `m_Prefix` and `m_Id` to get the directory name `mppm<id>`.
`<PROJECT_ROOT>/Library/VP/mppm<id>` is that player's project root.

Log file location depends on how the main Editor was launched:

- Without `-logFile`: `<PROJECT_ROOT>/Library/VP/mppm<id>/Logs/Editor.log`.
- With `-logFile <path>`: `<path without .txt>-mppm<id>.txt`.

To read a player's Console without depending on either location, run
`uloop --project-path <PROJECT_ROOT>/Library/VP/mppm<id> get-logs`.

## Command a Virtual Player

Each Virtual Player is an independent Unity Editor, so pass its project root to any command:

```bash
uloop --project-path <PROJECT_ROOT>/Library/VP/mppm<id> control-play-mode --action Status
uloop --project-path <PROJECT_ROOT>/Library/VP/mppm<id> get-logs
uloop --project-path <PROJECT_ROOT>/Library/VP/mppm<id> simulate-keyboard --action Press --key Space
```

uloop's one-command-at-a-time rule is per Editor, so commands to different players (or to the
main Editor) do not block each other.

## Hot reload

- A hot-reload patch lives in the Editor process it was applied to. To patch a Virtual
  Player, send the command to that player:
  `uloop --project-path <PROJECT_ROOT>/Library/VP/mppm<id> hot-reload --files ...`.
- A player reads the main project's `Library/ScriptAssemblies` and keeps its own hot-reload
  state (the `ActivePatchTotal` that `--status` reports, and the source snapshots) under its own
  `Library/UloopHotReload/`.
- A patch applied to the main Editor does not reach the players, and a patch applied to a player
  does not reach the main Editor or the other players. A compile of the main Editor's project
  reaches every player, and clears the patches a player holds.
- When the main Editor's project has never compiled the edited assembly, a player reports the
  file as `Failed` with `Compiled assembly not found ... Compile the main Editor's project
  first.`

## When a player quits right after activation

A player whose Editor shows
`Fatal Error! Compilation Pipeline: Could not read file Packages/<package>/<path>.asmdef`
a few seconds after it was activated, and then quits, never loaded its packages. Multiplayer
Play Mode gives a player symlinks to `Assets` and `ProjectSettings` and an empty `Packages`
folder (that is normal), and starts its Editor with `-noUpm -upmRestorePackages`: the player
takes the package list from the main project's `Library/PackageManager/ProjectCache` instead
of resolving packages itself. When the main Editor showed `Project has invalid dependencies` at
startup (for example a `file:` dependency whose folder is missing), that file is absent and the
player finds no package. Fix the main project's dependencies, restart the main Editor until it
starts without that dialog and `<PROJECT_ROOT>/Library/PackageManager/ProjectCache` exists,
then activate the player again. Until then
`uloop --project-path <PROJECT_ROOT>/Library/VP/mppm<id> ...` fails at project resolution: the
player never loaded uloop's project runner.

## Known limitations

- `Stop` sent while Virtual Players are still starting (the main Editor is not yet in Play Mode)
  does nothing. Wait for `Play` to return, then send `Stop`.
- A configuration that fails Unity's synchronous check (`IsConfigurationValid`, for example
  duplicate instance names) is rejected immediately with a tool error:
  `Play Mode configuration '<name>' cannot start: <reason>`.
- A scenario that fails Unity's pre-start validation (`Scenario.ValidateForRunningAsync`, for
  example an instance that cannot launch) makes Unity show the modal dialog
  "Play Mode Scenario - Scenario Setup Error". The CLI cannot see that failure and waits for Play
  Mode until `--timeout-seconds` expires. Close the dialog in the Editor and read the Console
  errors (`uloop get-logs --log-type Error`).
- `--unsaved-changes` applies as with the default configuration: `keep` (the default) starts
  without saving, `save` writes unsaved scenes first (an Untitled scene then fails with
  `CONTROL_PLAY_MODE_UNSAVED_CHANGES`), and `fail` stops if any exist.
