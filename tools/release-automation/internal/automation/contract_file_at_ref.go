package automation

import (
	"bytes"
	"context"
	"errors"
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
// the primary path is genuinely absent at the given ref.
//
// Absence is classified by exit-code-based existence checks against git rather
// than by matching git's stderr strings, which vary across git versions and
// locales.
func contractFileAtRefWithLegacyFallback(
	ctx context.Context,
	repoRoot string,
	ref string,
	primaryFile string,
	legacyFile string,
) (string, error) {
	content, showErr := protocolMinimumVersionFileAtRef(ctx, repoRoot, ref, primaryFile)
	if showErr == nil {
		return content, nil
	}

	// The primary path exists at the ref: `show` must have failed for a real
	// reason (permissions, corruption, ...). Never silently fall back.
	primaryExists, existsErr := fileExistsAtRef(ctx, repoRoot, ref, primaryFile)
	if existsErr != nil {
		// The probe failure prevents us from classifying the show failure,
		// but the original show error is still the useful signal. Surface
		// both so operators do not lose the real failure text.
		return "", fmt.Errorf("failed to check %s at %s: %w (original show error: %v)", primaryFile, ref, existsErr, showErr)
	}
	if primaryExists {
		return "", showErr
	}

	// A bad ref must not be misread as "file missing"; propagate the original
	// error so callers surface the real failure.
	refExists, refErr := refExistsAtRef(ctx, repoRoot, ref)
	if refErr != nil {
		// See the fileExistsAtRef branch above: the probe failed, but the
		// show error is still the useful signal. Surface both.
		return "", fmt.Errorf("failed to check ref %s: %w (original show error: %v)", ref, refErr, showErr)
	}
	if !refExists {
		return "", showErr
	}

	return protocolMinimumVersionFileAtRef(ctx, repoRoot, ref, legacyFile)
}

// fileExistsAtRef reports whether file is tracked at ref by running
// `git cat-file -e ref:file`. A non-zero git exit means "not present" and is
// not an execution failure. Any error that is not a git-reported non-zero
// exit (e.g. git binary missing) is returned so callers do not treat it as
// absence.
func fileExistsAtRef(ctx context.Context, repoRoot string, ref string, file string) (bool, error) {
	command := exec.CommandContext(ctx, "git", "-C", repoRoot, "cat-file", "-e", ref+":"+file)
	command.Dir = filepath.Clean(repoRoot)
	err := command.Run()
	if err == nil {
		return true, nil
	}
	var exitErr *exec.ExitError
	if errors.As(err, &exitErr) {
		// A probe killed by context cancellation also surfaces as an
		// ExitError (signal: killed), so classify it as an execution failure
		// rather than absence; misreading it as "missing" could make the
		// dispatcher guard skip validation as if the base were an initial
		// contract.
		if ctxErr := ctx.Err(); ctxErr != nil {
			return false, fmt.Errorf("git cat-file -e %s:%s interrupted: %w", ref, file, ctxErr)
		}
		return false, nil
	}
	return false, fmt.Errorf("git cat-file -e %s:%s failed: %w", ref, file, err)
}

// refExistsAtRef reports whether ref resolves to a commit by running
// `git rev-parse --verify --quiet ref^{commit}`. The `^{commit}` peel ensures
// the check fails when ref is not resolvable as a commit-ish. Exit-code
// semantics mirror fileExistsAtRef.
func refExistsAtRef(ctx context.Context, repoRoot string, ref string) (bool, error) {
	command := exec.CommandContext(ctx, "git", "-C", repoRoot, "rev-parse", "--verify", "--quiet", ref+"^{commit}")
	command.Dir = filepath.Clean(repoRoot)
	err := command.Run()
	if err == nil {
		return true, nil
	}
	var exitErr *exec.ExitError
	if errors.As(err, &exitErr) {
		// See fileExistsAtRef: a cancellation kill is an ExitError too and
		// must not be classified as a missing ref.
		if ctxErr := ctx.Err(); ctxErr != nil {
			return false, fmt.Errorf("git rev-parse --verify %s interrupted: %w", ref, ctxErr)
		}
		return false, nil
	}
	return false, fmt.Errorf("git rev-parse --verify %s failed: %w", ref, err)
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
