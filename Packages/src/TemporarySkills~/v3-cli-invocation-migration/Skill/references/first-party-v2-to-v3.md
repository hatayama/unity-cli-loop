# First-Party V2 to V3 CLI Migration

Use this reference after the detector finds a candidate. Keep edits local to the command or parser being migrated.

## Boolean Argument Rules

| V2 form | V3 form |
| --- | --- |
| `--flag true` | `--flag` when the V3 option is a positive default-false boolean |
| `--flag=false` | remove the option when the V3 default is already false |
| `--flag true` | remove the option when the V3 default is already true |
| `--flag false` | use the V3 negative option when the V3 default is true |

## Special First-Party Options

| V2 command | V2 option | V3 replacement |
| --- | --- | --- |
| `uloop compile` | `--force-recompile true` | `--force-recompile` |
| `uloop compile` | `--force-recompile false` | remove |
| `uloop compile` | `--wait-for-domain-reload true` or bare `--wait-for-domain-reload` | remove |
| `uloop compile` | `--wait-for-domain-reload false` | `--no-wait-for-domain-reload` |
| `uloop compile` | `--reload-external-scene-changes true` | remove |
| `uloop compile` | `--reload-external-scene-changes false` | `--stop-on-external-scene-changes` |
| `uloop run-tests` | `--save-before-run true` or bare `--save-before-run` | remove |
| `uloop run-tests` | `--save-before-run false` | `--fail-on-unsaved-changes` |
| `uloop record-input` | `--show-overlay true` | remove |
| `uloop record-input` | `--show-overlay false` | `--no-show-overlay` |
| `uloop replay-input` | `--show-overlay true` | remove |
| `uloop replay-input` | `--show-overlay false` | `--no-show-overlay` |
| `uloop get-hierarchy` | `--include-components true` | remove |
| `uloop get-hierarchy` | `--include-components false` | `--no-include-components` |
| `uloop get-hierarchy` | `--include-inactive true` | remove |
| `uloop get-hierarchy` | `--include-inactive false` | `--no-include-inactive` |
| `uloop execute-dynamic-code` | `--compile-only true` | `--compile-only` |
| `uloop execute-dynamic-code` | `--compile-only false` | remove |

## Output JSON Field Candidates

V3 first-party CLI output generally uses camelCase JSON fields. Only update these when the surrounding parser is reading `uloop` output.

| V2 field | V3 field |
| --- | --- |
| `Success` | `success` |
| `Message` | `message` |
| `ErrorMessage` | `errorMessage` |
| `ErrorCount` | `errorCount` |
| `WarningCount` | `warningCount` |
| `TotalCount` | `totalCount` |
| `DisplayedCount` | `displayedCount` |
| `LogType` | `logType` |
| `StackTrace` | `stackTrace` |
| `XmlPath` | `xmlPath` |
| `TestCount` | `testCount` |
| `PassedCount` | `passedCount` |
| `FailedCount` | `failedCount` |
| `SkippedCount` | `skippedCount` |
| `CompletedAt` | `completedAt` |
| `ScreenshotCount` | `screenshotCount` |
| `Screenshots` | `screenshots` |
| `CompilationErrors` | `compilationErrors` |
| `ErrorCode` | `errorCode` |
| `UpdatedCode` | `updatedCode` |
| `DiagnosticsSummary` | `diagnosticsSummary` |
| `OutputPath` | `outputPath` |
| `InputPath` | `inputPath` |
| `TotalFrames` | `totalFrames` |
| `DurationSeconds` | `durationSeconds` |
| `CurrentFrame` | `currentFrame` |
| `IsReplaying` | `isReplaying` |
| `KeyName` | `keyName` |
| `PositionX` | `positionX` |
| `PositionY` | `positionY` |
| `EndPositionX` | `endPositionX` |
| `EndPositionY` | `endPositionY` |
| `HitGameObjectName` | `hitGameObjectName` |
| `IsPlaying` | `isPlaying` |
| `IsPaused` | `isPaused` |
| `ClearedLogCount` | `clearedLogCount` |
| `ClearedCounts` | `clearedCounts` |

## Review Guidance

- `jq '.Success'` usually becomes `jq '.success'`.
- PowerShell `$result.Success` usually becomes `$result.success`.
- If the code handles non-uloop JSON too, narrow the edit or leave it unchanged and report it.
- Removed commands such as `get-project-info` and `get-version` require manual replacement because the correct V3 behavior depends on the caller's intent.
