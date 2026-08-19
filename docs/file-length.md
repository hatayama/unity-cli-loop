# File Length Checks

This repository limits production source files to 500 source lines of code (SLOC).
SLOC is the number of lines that are neither blank nor comment-only. Line comments
(`//`, including C# `///`) and block comments (`/* */`) are excluded. Comment
markers inside string literals are code, not comments.

## Scope

The checker walks only these trees:

- `Packages/src/**/*.cs`, including Unity-ignored `~` folders
- `cli/**/*.go`
- `tools/**/*.cs`

It excludes tests and fixtures with one list, owned by the Go tool:

- any path containing `/Tests/`
- any path containing `/testdata/`
- `*_test.go`
- `Assets/` and every other tree, by not walking them

`partial class` is not a permitted way to satisfy the limit. Split files with
real Extract Class / Move Method so each type keeps one source file.

## Local Usage

Run the check:

```sh
scripts/check-file-length.sh
```

Use a different limit:

```sh
CODE_FILE_LENGTH_MAX_LENGTH=600 scripts/check-file-length.sh
```

Fail on findings:

```sh
CODE_FILE_LENGTH_FAIL_ON_EXCEEDED=true scripts/check-file-length.sh
```

Run the Go tool directly:

```sh
(cd cli/release-automation && go run ./cmd/check-file-length --root ../.. --max-length 500)
```

## Operating Policy

The repository-wide maximum file length is 500 SLOC. It is declared in two
places that must stay in step:

1. `MAX_FILE_LENGTH` in `scripts/check-file-length.sh`
2. `DefaultMaxFileLength` in `cli/release-automation/internal/automation/file_length.go`

The `File Length Report` job on the `Code Complexity` workflow reports files
over the limit and does not fail the pull request yet. Locally the check stays
in warning mode unless `CODE_FILE_LENGTH_FAIL_ON_EXCEEDED=true` is set.

When touching a reported file, split it below the limit before adding behavior.
A new over-limit file in a change under review is a required fix, not noise to
defer.

CRLF and LF encodings of the same source must produce the same SLOC. A UTF-8
BOM is ignored and does not count as a source line.
