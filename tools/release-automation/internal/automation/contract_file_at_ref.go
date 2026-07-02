package automation

import (
	"bytes"
	"context"
	"fmt"
	"os/exec"
	"path/filepath"
	"strings"
)

func runnerContractMissingAtReleaseMessage(releaseTag string) string {
	return fmt.Sprintf("project runner release %s does not provide %s or %s", releaseTag, cliContractFile, legacyRunnerContractFile)
}

// runnerContractFileAtRef reads the CLI/runner IPC contract file at a git ref.
func runnerContractFileAtRef(ctx context.Context, repoRoot string, ref string) (string, error) {
	return contractFileAtRefWithLegacyFallback(ctx, repoRoot, ref, cliContractFile, legacyRunnerContractFile)
}

// contractFileAtRefWithLegacyFallback reads a release contract at a git ref.
// Release tags published before the cli/ directory split still provide their
// contracts at the pre-split paths, so this falls back to the legacy path when
// the primary path is missing at the given ref.
func contractFileAtRefWithLegacyFallback(
	ctx context.Context,
	repoRoot string,
	ref string,
	primaryFile string,
	legacyFile string,
) (string, error) {
	content, err := protocolMinimumVersionFileAtRef(ctx, repoRoot, ref, primaryFile)
	if err == nil {
		return content, nil
	}
	if !isMissingFileAtRefError(err, primaryFile) {
		return "", err
	}
	return protocolMinimumVersionFileAtRef(ctx, repoRoot, ref, legacyFile)
}

// isMissingFileAtRefError reports whether err came from `git show ref:file`
// failing because file does not exist at ref, as opposed to any other git
// failure (auth, network, etc.) that must not be silently swallowed.
// Current git prints the lowercase "path ..." forms; the capitalized
// "Path ... does not exist in" form is kept for older git versions.
func isMissingFileAtRefError(err error, file string) bool {
	message := err.Error()
	quotedPath := "'" + file + "'"
	return strings.Contains(message, "path "+quotedPath+" exists on disk, but not in") ||
		strings.Contains(message, "path "+quotedPath+" does not exist in") ||
		strings.Contains(message, "Path "+quotedPath+" does not exist in")
}

func protocolMinimumVersionFileAtRef(
	ctx context.Context,
	repoRoot string,
	ref string,
	file string,
) (string, error) {
	return runProtocolMinimumVersionOutput(
		ctx,
		repoRoot,
		"git",
		"-C",
		repoRoot,
		"show",
		ref+":"+file)
}

func runProtocolMinimumVersionOutput(
	ctx context.Context,
	workDir string,
	name string,
	args ...string,
) (string, error) {
	command := exec.CommandContext(ctx, name, args...)
	command.Dir = filepath.Clean(workDir)
	stdout := bytes.Buffer{}
	stderr := bytes.Buffer{}
	command.Stdout = &stdout
	command.Stderr = &stderr
	err := command.Run()
	if err != nil {
		return "", fmt.Errorf("%s %s failed: %w\n%s%s", name, strings.Join(args, " "), err, stderr.String(), stdout.String())
	}
	return stdout.String(), nil
}
