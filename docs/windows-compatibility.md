# Windows compatibility guardrails

Most day-to-day development happens on macOS, but this project must keep
working on Windows. Before changing scripts, skill files, generated-file
synchronization, path handling, or text parsing, assume Windows will expose
bugs that macOS hides.

- Treat encoding as explicit input. When PowerShell reads UTF-8 repository files, pass `-Encoding UTF8`; Windows PowerShell 5.1 otherwise uses a legacy default that can corrupt non-ASCII text and even report wrong line numbers.
- Repository text files should use LF by default. Only keep CRLF when a specific tool or file format requires it. Preserve expected line endings when writing generated files, and normalize line endings before comparison only when logical text equality is intended. If a script fails only under bash, WSL, or Git Bash, check CRLF before changing logic.
- Normalize relative paths at API boundaries. Do not compare raw path strings that may contain `/` on one side and `\` on another. Convert separators before storing, comparing, deleting, or syncing generated files.
- Prefer forward slashes in JSON `file:` paths and other cross-platform config values. Use escaped backslashes only when the target format explicitly requires them.
- Validate Windows-facing PowerShell with both `pwsh` and Windows PowerShell when practical, especially for multiline arguments, here-strings, UTF-8 files, and native executable calls.
- When validating this checkout on Windows, use the repo-local native binary (`dist/windows-amd64/uloop.exe`) instead of a `PATH`-resolved `uloop`. If a bash validation command cannot see the expected Go toolchain on Windows, retry through a login shell such as `bash -lc`.
- Add or update a regression test whenever a fix depends on encoding, line endings, or separator normalization. A passing macOS test alone is not enough for these cases.

## Microsoft Defender false positives

### Symptom

Defender's ML detections (names ending in `!ml`, e.g. `Trojan:Win32/Bearfoos.A!ml`) can quarantine `uloop.exe` while a command is running. From the agent's side this looks like `UNITY_NOT_REACHABLE` or an abrupt process exit, not like an antivirus event. Check `Get-MpThreatDetection` in PowerShell, or Windows Security → Protection history, before debugging IPC. Whether a given release is flagged depends on the security-intelligence version, not only on the binary. See [issue #2503](https://github.com/hatayama/unity-cli-loop/issues/2503).

```powershell
Get-MpThreatDetection
```

### Verify the binary is genuine before allowing it

Hash the installed dispatcher and compare it to the `uloop.exe` inside the matching GitHub Release zip. Hash the zip itself against the accompanying `.sha256` file. Then verify the zip attestation.

```powershell
gh release download dispatcher-v<version> --repo hatayama/unity-cli-loop -p "uloop-dispatcher-windows-amd64.zip*"
Get-Content -Encoding UTF8 .\uloop-dispatcher-windows-amd64.zip.sha256
certutil -hashfile .\uloop-dispatcher-windows-amd64.zip SHA256
Expand-Archive -Path .\uloop-dispatcher-windows-amd64.zip -DestinationPath .\uloop-verify -Force
certutil -hashfile .\uloop-verify\uloop.exe SHA256
certutil -hashfile "$env:LOCALAPPDATA\Programs\uloop\bin\uloop.exe" SHA256
gh attestation verify uloop-dispatcher-windows-amd64.zip --repo hatayama/unity-cli-loop
```

The zip hash must match the `.sha256` file, and the installed `uloop.exe` hash must match the `uloop.exe` inside the zip. If either hash differs, do not allow the binary. Open an issue instead.

### Restore from quarantine

Restoration needs an elevated prompt. Use Windows Security → Protection history → Restore, or:

```powershell
& "C:\Program Files\Windows Defender\MpCmdRun.exe" -Restore -ListAll
& "C:\Program Files\Windows Defender\MpCmdRun.exe" -Restore -FilePath "<path to uloop.exe>"
```

If the quarantined file was the genuine dispatcher, reinstalling with `scripts/install.ps1` is often faster than restoring.

### Prevent re-detection

Restore alone does not stop the next run from being quarantined again. "Allow on device" is scoped to a `ThreatID`, so a later release that gets a different detection name can be flagged again. After verifying the binary is genuine, add path exclusions from an elevated PowerShell session:

```powershell
Add-MpPreference -ExclusionPath "$env:LOCALAPPDATA\Programs\uloop\bin\uloop.exe"
Add-MpPreference -ExclusionPath "$env:LOCALAPPDATA\uloop"
if ($env:ULOOP_CACHE_DIR) { Add-MpPreference -ExclusionPath $env:ULOOP_CACHE_DIR }
```

The cache root stays a directory exclusion because each project-runner release lives under its own versioned path. Remove the exclusions when they are no longer needed:

```powershell
Remove-MpPreference -ExclusionPath "$env:LOCALAPPDATA\Programs\uloop\bin\uloop.exe"
Remove-MpPreference -ExclusionPath "$env:LOCALAPPDATA\uloop"
if ($env:ULOOP_CACHE_DIR) { Remove-MpPreference -ExclusionPath $env:ULOOP_CACHE_DIR }
```

### What the build does about it

Windows binaries carry a `VERSIONINFO` resource generated by `scripts/build-go-cli.sh` from the release version (`ProductName` / `CompanyName` / `FileVersion` / `ProductVersion`). `scripts/check-windows-version-info.ps1` verifies it in CI. The binaries are not Authenticode-signed.

## Offline compile check on Windows

`uloop compile-check` (see `docs/compile-check.md`) reads Bee response files, walks the project
tree, and compares paths against what those files recorded, so it is exactly the class of feature
this document is about. It was verified on Windows 11 with a Hub-installed Unity 6 Editor against
a project with several hundred assemblies: `--all`, a run with no edits, a deliberate compile
error, and the replay of that error on the next run all behaved as they do on macOS.

What the implementation relies on, and what the Windows run observed:

- **Editor layout.** The layout is probed, not chosen from `runtime.GOOS`. The Editor's content
  root is the macOS `Contents` directory when the executable sits in a `MacOS` directory inside it,
  and otherwise a `Data` directory beside the executable. The compiler directory under that root is
  found by a bounded scan, so a layout that moved is reported rather than silently missed. The one
  place `runtime.GOOS` is consulted is the host file name (`dotnet.exe` versus `dotnet`). A Hub
  install on Windows does keep `Data` beside `Unity.exe`, and both `csc` and `dotnet.exe` are found.
- **Separators in response files.** Bee writes forward slashes inside `.rsp` files on Windows too,
  for project-relative and absolute paths alike (`C:/Program Files/Unity/...`). The parser keeps
  every path exactly as Bee spelled it, rebuilt source lists are spelled with `/` as well
  (`recordSourcePath`), and the response file compile-check writes uses `/` for the paths it adds.
  Why the spelling matters beyond comparisons: csc passes each source path to analyzers verbatim,
  so an analyzer that excludes files by a `/`-separated path prefix behaves differently when it is
  handed `\`. The Go file APIs accept `/` on Windows, so the paths need no conversion before they
  reach the OS.
- **Diagnostic paths.** csc on Windows prints diagnostic paths with `\` even when the response file
  spells them with `/`, so the `File` field reads like `Assets\Scripts\Foo.cs` — a project-relative
  Windows path, which is also how Unity's own compile reports it.

The compile-check tests assert the `/` spelling, so the Windows CI job running `go test` catches a
regression that converts response-file paths to the OS separator; a macOS run cannot.

To verify again, on a Windows machine with a Unity project that has been built at least once:

1. Build the binary for `windows-amd64` — `go build -o uloop.exe ./cmd/dispatcher` from
   `cli/dispatcher`, or `scripts/build-go-cli.sh` from a shell that can run it, which writes
   `dist/windows-amd64/uloop.exe`. Then run `.\dist\windows-amd64\uloop.exe compile-check --all`
   — a bare `uloop.exe` would resolve to an installed release rather than the build under test.
2. Confirm the Editor was discovered — a run that fails at Editor discovery reports it before any
   compile happens.
3. Open one `Library\Bee\artifacts\<dag>\*.rsp` and check the separator in its `-out:` line.
4. Run `compile-check` without `--all` and no edits, and confirm it reports that no assembly changed.
5. Introduce a deliberate compile error and check the separator in the diagnostic's `File` field.

## `launch --restart` and the Temp directory

### Symptom

`uloop launch -r` fails before Unity is relaunched:

```
INTERNAL_ERROR: unlinkat <PROJECT_ROOT>\Temp\FSTimeGet-...: The process cannot access the file because it is being used by another process.
```

Restart deletes the project's `Temp` directory once the old Editor has exited. On Windows the Editor's exit does not mean every handle under `Temp` is released: helper processes it spawned (the build backend, the shader compiler, the IL post-processor) can outlive it, and Defender may still be scanning a file it just saw written. Deleting `Temp` then fails with a sharing violation. The window is widest during an asset import, which is where the report above came from.

### How restart handles it

- Only `Temp/UnityLockfile` has to go. A leftover lockfile makes the next Editor refuse to open the project as already opened; everything else under `Temp` is a cache Unity recreates on startup.
- The lockfile is deleted with a short retry (250 ms apart, up to 5 seconds) so a handle that is about to be released does not fail the restart. If it still cannot be deleted, restart stops with an error and does not launch Unity.
- The rest of `Temp` is deleted on a best-effort basis. A failure prints one warning line to stderr and the launch continues.
- Windows kills the Editor with `taskkill /PID <pid> /T /F` so its helper processes go down with it, instead of leaving them holding files. If `taskkill` cannot be run, restart falls back to killing the Editor process alone.

Unix does not need any of this: an open file can be unlinked there, so a helper process that outlives the Editor does not block the Temp cleanup.
