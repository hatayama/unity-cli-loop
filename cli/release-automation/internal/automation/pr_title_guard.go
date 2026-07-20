package automation

import (
	"fmt"
	"io"
	"regexp"
	"strings"
)

// allowedPRTitleTypes lists the conventional commit types release-please
// recognizes when parsing squash-merge commit subjects into release notes.
var allowedPRTitleTypes = []string{
	"build", "chore", "ci", "docs", "feat", "fix", "perf", "refactor", "revert", "style", "test",
}

// conventionalCommitTitlePattern matches "<type>(<scope>)?!?: <summary>" with
// a single space after the colon and a non-empty summary. The type token is
// captured loosely here; allowedPRTitleTypes is checked separately so the
// error message can report the exact unmatched type when it exists.
var conventionalCommitTitlePattern = regexp.MustCompile(`^([a-z]+)(\([^()]+\))?!?: (.+)$`)

// CheckPRTitle reports whether title is a conventional commit header using
// one of allowedPRTitleTypes, returning the user-facing violation message
// when it is not. This repository squash-merges every PR, so the PR title
// becomes the base branch commit subject that release-please parses; a
// non-conventional title silently drops the change from releases and
// changelogs (as happened for PR #1884). The message is returned as a plain
// string rather than an error because it is a fixed, sentence-final
// user-facing string, not a Go error to be wrapped or compared.
func CheckPRTitle(title string) (bool, string) {
	trimmedTitle := strings.TrimSpace(title)

	matches := conventionalCommitTitlePattern.FindStringSubmatch(trimmedTitle)
	if matches == nil || strings.TrimSpace(matches[3]) == "" {
		return false, formatPRTitleViolation(title)
	}

	commitType := matches[1]
	for _, allowedType := range allowedPRTitleTypes {
		if commitType == allowedType {
			return true, ""
		}
	}

	return false, formatPRTitleViolation(title)
}

// RunPRTitleGuard validates prTitle and reports the outcome to stdout/stderr,
// returning the process exit code the caller should use.
func RunPRTitleGuard(stdout io.Writer, stderr io.Writer, prTitle string) int {
	if isValid, violationMessage := CheckPRTitle(prTitle); !isValid {
		_, _ = fmt.Fprintln(stderr, violationMessage)
		return 1
	}

	_, _ = fmt.Fprintln(stdout, "PR title guard passed.")
	return 0
}

func formatPRTitleViolation(title string) string {
	return fmt.Sprintf(
		"PR title %q is not a conventional commit header. This repository squash-merges, so the PR title becomes the commit subject that release-please parses; a non-conventional title silently drops the change from releases and changelogs. Retitle the PR as <type>(<scope>)?: <summary> using one of: %s.",
		title,
		strings.Join(allowedPRTitleTypes, ", "),
	)
}
