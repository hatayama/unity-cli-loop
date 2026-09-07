# Enum Value Casing on the CLI

Date: 2026-09-07

## Decision

Enum-valued CLI parameters (`--action`, `--log-type`, `--test-mode`, ...) keep the spelling of
the C# enum member that backs them, and that member is named by the following rules:

1. A value that names a Unity concept keeps Unity's spelling: `EditMode` / `PlayMode`
   (Test Runner), `Error` / `Warning` / `Log` (`LogType`), `KeyDown` / `KeyUp`
   (`EventType`), `Play` / `Pause` / `Step` (Editor play controls), `GameView` (window).
2. An enum whose members are all single-word values the CLI itself defines uses lowercase
   members: `start` / `stop` / `status`, `low` / `medium` / `high`, `exact` / `prefix` /
   `contains`, `save` / `fail` / `discard`, `window` / `rendering` / `auto`.
3. An enum with at least one multi-word member, or at least one Unity-derived member, keeps
   PascalCase for every member, so one enum never mixes styles: `Click` / `LongPress` /
   `MoveDelta`, `Press` / `KeyDown` / `ReleaseAll`, `Play` / `Stop` / `Status` / `Resume`.
4. Options that exist only in the Go CLI, with no C# enum behind them, use lowercase
   kebab-case: `pre-line` / `post-line`, `single-shot` / `continuous` / `trace`.

Parsing stays case-insensitive on both sides (`CaseInsensitiveStringEnumConverter` in the
package, `strings.EqualFold` in the project runner), so `--action Status` and
`--action status` are the same request. The rules govern the documented spelling, which is
what `--help`, `uloop list`, the tool catalog, and skill files show and what AI agents copy.

We do not add a conversion layer that turns C# member names into kebab-case wire values.

## Context

POSIX-style CLIs almost always spell enum values in lowercase, and issue #2649 proposed
converting every value to lowercase / kebab-case (`key-down`, `edit-mode`, `game-view`) for
that reason. Working through the inventory showed that the majority of values here are Unity
vocabulary rather than CLI vocabulary: `LogType` members, Test Runner modes, `EventType`
names, Editor play controls, mouse button names, and an Editor window name. Lowercasing those
makes the CLI disagree with the Unity documentation an agent reads next to it, while a
"Unity-derived values stay, CLI-own values change" split leaves only a handful of values
(`Exact` / `Path` / `Regex`, `LongPress` / `MoveDelta`, `DragStart` / `DragEnd`) to convert
and produces no visible consistency.

A mechanical kebab-case layer would also introduce a second representation of every enum that
the schema generator, the JSON converter, the response echo fields, the Go validator, and the
catalog must all agree on, for a benefit that case-insensitive parsing already delivers to
anyone who prefers typing lowercase.

The rules above describe what the repository already did for every enum except two, so they
codify practice rather than start a migration. The two exceptions
(`find-game-objects --search-mode` and `replay-input --action`, both all-single-word CLI-own
enums spelled in PascalCase) are corrected under rule 2.

## Consequences

- New tools follow the rules when naming their enum members; `record-video`
  (`start` / `stop` / `status`, `low` / `medium` / `high`) is the reference for rule 2.
- Renaming an existing enum member under these rules is a documentation-visible change but
  not a breaking one: the old spelling is still accepted case-insensitively. Response fields
  that echo the enum (`Action`, `Quality`) change their casing, so tests asserting on them
  must be updated in the same change.
- Skill files, the generated catalog, and `Packages/src/Documentation~/tools*.md` must use the
  member spelling verbatim, so a reader sees one form everywhere.

## Reversal condition

Reopen this decision only if a consumer appears that cannot rely on case-insensitive parsing
(for example a strict JSON schema validator in front of the tool catalog, or a shell
completion generator that must emit a single canonical form) and it needs lowercase values
for Unity-derived terms as well. A preference for lowercase alone is not a reversal
condition; it is satisfied today by typing lowercase.
