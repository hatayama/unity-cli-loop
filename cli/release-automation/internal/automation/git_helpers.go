package automation

import (
	"context"
	"errors"
	"fmt"
	"os/exec"
	"path/filepath"
	"strings"
)

// gitMergeBase returns the merge-base commit of baseRef and headRef.
func gitMergeBase(ctx context.Context, repoRoot string, baseRef string, headRef string) (string, error) {
	command := exec.CommandContext(ctx, "git", "-C", repoRoot, "merge-base", baseRef, headRef)
	output, err := command.Output()
	if err != nil {
		return "", fmt.Errorf("git merge-base %s %s failed: %w", baseRef, headRef, err)
	}
	return strings.TrimSpace(string(output)), nil
}

// gitFileAtRef returns the file contents at ref. ok is false when the path is
// absent at that commit so callers can treat a newly added catalog as a
// structural change instead of a show error.
func gitFileAtRef(ctx context.Context, repoRoot string, ref string, path string) ([]byte, bool, error) {
	command := exec.CommandContext(ctx, "git", "-C", repoRoot, "show", ref+":"+path)
	command.Dir = filepath.Clean(repoRoot)
	output, err := command.Output()
	if err == nil {
		return output, true, nil
	}
	var exitErr *exec.ExitError
	if errors.As(err, &exitErr) {
		if ctxErr := ctx.Err(); ctxErr != nil {
			return nil, false, fmt.Errorf("git show %s:%s interrupted: %w", ref, path, ctxErr)
		}
		return nil, false, nil
	}
	return nil, false, fmt.Errorf("git show %s:%s failed: %w", ref, path, err)
}
