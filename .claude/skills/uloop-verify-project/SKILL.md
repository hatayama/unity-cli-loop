---
name: uloop-verify-project
description: "Check a Unity project's files without starting Unity: missing or orphaned .meta files, invalid or duplicate GUIDs, leftover merge conflict markers, and a broken Packages/manifest.json. Use after adding, moving, renaming, deleting, or merging assets, and before committing."
---

# uloop verify-project

Check the project's files the way Unity reads them on its next import, without starting Unity.
When Unity opens a project with these problems, it silently gives assets new GUIDs and deletes
orphaned `.meta` files, which breaks references in ways that cannot be undone. Run this first and
fix what it finds while the old GUIDs still exist.

## When to use

- After adding, moving, renaming, copying, or deleting assets, especially from a script or another branch
- After a merge or rebase that touched `Assets/`, `Packages/`, or `ProjectSettings/`
- Before committing
- Before opening the project in Unity

## Usage

```bash
uloop verify-project
uloop verify-project --project-path <path>
```

Exit codes:

- `0`: nothing found
- `1`: problems found (the report is still printed on stdout), or the check could not run (an error
  envelope on stderr and nothing on stdout)

## Checks

| Check | Meaning | How to fix |
|-------|---------|------------|
| `CONFLICT_MARKER` | A merge conflict block (`<<<<<<<`, `=======`, `>>>>>>>`) is left in a file | Resolve the conflict and remove the markers; Unity cannot load or compile a file that contains them |
| `MANIFEST_INVALID` | `Packages/manifest.json` is missing, is not a valid JSON object, has a malformed `dependencies` object, or has a `file:` dependency that does not point to a package folder | Restore the manifest from version control, or fix or remove the dependency the message names |
| `GUID_DUPLICATE` | Two or more `.meta` files declare the same GUID | Keep the `.meta` file of the original asset and delete the copies' `.meta` files so Unity assigns them new GUIDs |
| `GUID_INVALID` | A `.meta` file has no valid `guid:` line (32 hexadecimal characters, not all zero) | Restore the `.meta` file from version control; otherwise Unity assigns a new GUID, which breaks references to the asset |
| `META_MISSING` | An asset has no `.meta` file next to it | Bring its `.meta` file along from the branch or folder it came from; if you just created the asset, let Unity import it (for example with `uloop compile`) and commit the generated `.meta` file |
| `META_ORPHAN` | A `.meta` file has no matching asset | If the asset was moved or renamed, move the `.meta` file with it to keep the GUID; if the asset was deleted, delete the `.meta` file too |

## What is checked

- `.meta` files under `Assets/`, under every embedded package (a folder directly under `Packages/`
  with a `package.json` file), and under every local package the manifest lists as `file:`
  (a relative path is resolved from `Packages/`). A folder inside another of these is checked once.
- Names Unity hides are skipped and need no `.meta` file: names starting with `.`, names ending
  with `~`, `cvs` in any case, and files with a `.tmp` extension in any case.
- Plugin folders ending in `.bundle`, `.framework`, `.xcframework`, `.plugin`, or `.androidlib`
  (any case) need their own `.meta` file, but nothing inside them is checked: Unity imports such a
  folder as one plugin, so the files inside are not checked whether they have `.meta` files or not.
- Symbolic links need a `.meta` file but are never followed.
- A `.meta` file pairs with its asset by exact name, letter case included, so the result is the same
  on every OS. An orphaned `.meta` file is not checked further, because Unity deletes it.
- Conflict markers are searched in every file of those folders (`.meta` files included), every file
  under `ProjectSettings/`, and `Packages/manifest.json` and `Packages/packages-lock.json`. Only the
  first block of each file is reported. A file with a NUL byte in its first 8000 bytes is treated as
  binary and skipped.

## Output

JSON on stdout:

- `Success`: `true` when nothing was found
- `ProjectRoot`: the checked project
- `FindingCount`: every problem found, including those left out of `Findings`
- `CountsByCheck`: the count for each of the six checks, always all six
- `Findings`: each with `Check`, `Path`, and `Message`, plus `Line` for `CONFLICT_MARKER` and
  `GUID` and `RelatedPaths` for `GUID_DUPLICATE`. `Path` is `/`-separated and relative to the
  project, or absolute for a local package outside it. Sorted in the order of the table above, then
  by path. At most 100 findings of each check are listed.
- `Truncated`: `true` when a check had more than 100 findings
- `ScannedRoots`: the folders whose `.meta` files were checked
- `MetaFileCount`: how many `.meta` files were checked
- `Message`: one-line summary

## Notes

- A file you just created has no .meta file until Unity imports it. Run `uloop compile` first, or treat META_MISSING on brand-new files as expected.
- After a fresh clone, an empty folder that git does not track leaves its folder .meta file as META_ORPHAN. Delete that .meta file, or add a file to the folder so git tracks it.
- Run it while Unity is idle. Files Unity is writing at that moment can be reported, and a file Unity deletes during the check makes the command fail; run it again once Unity is idle.
- Not checked: GUID collisions with packages in Library/PackageCache, folders hidden only by the Windows hidden attribute (they are reported as META_MISSING), and package folders under Packages/ that are symbolic links.
- It only reads files and works whether Unity is open or closed.
- If uloop answers that verify-project is an unknown command, or that it "is handled by the global uloop dispatcher", the installed uloop is older than this command: run `uloop update`.
