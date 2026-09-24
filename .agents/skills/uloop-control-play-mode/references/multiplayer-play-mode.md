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
- Unsaved scenes are saved quietly before `Play`, as with the default configuration. An Untitled
  scene still fails with `CONTROL_PLAY_MODE_UNSAVED_CHANGES`.
