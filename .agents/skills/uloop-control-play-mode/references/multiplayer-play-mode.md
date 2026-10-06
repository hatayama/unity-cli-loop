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

- A hot-reload patch lives in the Editor process it was applied to. A patch applied to the
  main Editor does not reach the Virtual Players, and a player's own
  `uloop --project-path <PROJECT_ROOT>/Library/VP/mppm<id> hot-reload --status` reports no
  active patch.
- Hot reload cannot patch a Virtual Player yet: a player loads the main project's
  `Library/ScriptAssemblies` and has none under its own root. `hot-reload --files ...` sent
  to a player reports the file as `Failed`. Whether the CLI then compiles in that player
  follows `--compile-on-skip`, as for any unapplied edit: when it compiles, the edit comes in
  (`Outcome` is `ReplacedByCompile`); when the compile is held (`CompileFallback` is
  `HeldForPlayMode`: `auto`, the default, while that player is in Play Mode), the edit has not
  reached the player.

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
