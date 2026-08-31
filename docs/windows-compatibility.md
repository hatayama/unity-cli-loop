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
