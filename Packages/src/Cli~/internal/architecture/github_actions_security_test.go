package architecture

import (
	"fmt"
	"os"
	"path/filepath"
	"regexp"
	"sort"
	"strings"
	"testing"
)

var githubActionCommitRefPattern = regexp.MustCompile(`^[0-9a-fA-F]{40}$`)

// Tests that remote GitHub Actions are pinned to immutable commit SHAs.
func TestWorkflowActionsUseCommitPins(t *testing.T) {
	repositoryRoot := findRepositoryRoot(t, findModuleRoot(t))
	violations := []string{}
	for _, workflowPath := range workflowFilePaths(t, repositoryRoot) {
		lines := readWorkflowLines(t, workflowPath)
		for lineIndex, line := range lines {
			actionRef, ok := parseUsesAction(line)
			if !ok || isLocalAction(actionRef) {
				continue
			}
			if githubActionCommitRefPattern.MatchString(actionReference(actionRef)) {
				continue
			}
			violations = append(violations, workflowViolation(repositoryRoot, workflowPath, lineIndex, actionRef, "pin remote actions to a full commit SHA"))
		}
	}
	if len(violations) > 0 {
		sort.Strings(violations)
		t.Fatalf("workflow action pinning policy violations:\n%s", strings.Join(violations, "\n"))
	}
}

// Tests that setup-go does not use cache in pull request workflows.
func TestPullRequestWorkflowsDisableSetupGoCache(t *testing.T) {
	repositoryRoot := findRepositoryRoot(t, findModuleRoot(t))
	violations := []string{}
	for _, workflowPath := range workflowFilePaths(t, repositoryRoot) {
		lines := readWorkflowLines(t, workflowPath)
		if !workflowRunsOnPullRequest(lines) {
			continue
		}
		for lineIndex, line := range lines {
			actionRef, ok := parseUsesAction(line)
			if !ok || actionRepository(actionRef) != "actions/setup-go" {
				continue
			}
			if stepContains(lines, lineIndex, "cache: false") {
				continue
			}
			violations = append(violations, workflowViolation(repositoryRoot, workflowPath, lineIndex, actionRef, "set cache: false for setup-go in pull request workflows"))
		}
	}
	if len(violations) > 0 {
		sort.Strings(violations)
		t.Fatalf("pull request setup-go cache policy violations:\n%s", strings.Join(violations, "\n"))
	}
}

// Tests that pull request workflow cache actions are guarded behind trusted Unity secrets.
func TestPullRequestWorkflowCacheActionsRequireTrustedUnitySecrets(t *testing.T) {
	repositoryRoot := findRepositoryRoot(t, findModuleRoot(t))
	violations := []string{}
	for _, workflowPath := range workflowFilePaths(t, repositoryRoot) {
		lines := readWorkflowLines(t, workflowPath)
		if !workflowRunsOnPullRequest(lines) {
			continue
		}
		for lineIndex, line := range lines {
			actionRef, ok := parseUsesAction(line)
			if !ok || actionRepository(actionRef) != "actions/cache" {
				continue
			}
			if stepContains(lines, lineIndex, "if: env.HAS_UNITY_LICENSE == 'true'") {
				continue
			}
			violations = append(violations, workflowViolation(repositoryRoot, workflowPath, lineIndex, actionRef, "guard pull request cache actions behind Unity license secrets"))
		}
	}
	if len(violations) > 0 {
		sort.Strings(violations)
		t.Fatalf("pull request cache action policy violations:\n%s", strings.Join(violations, "\n"))
	}
}

func workflowFilePaths(t *testing.T, repositoryRoot string) []string {
	t.Helper()
	workflowRoot := filepath.Join(repositoryRoot, ".github", "workflows")
	entries, err := os.ReadDir(workflowRoot)
	if err != nil {
		t.Fatalf("failed to read workflow directory: %v", err)
	}
	paths := []string{}
	for _, entry := range entries {
		if entry.IsDir() {
			continue
		}
		name := entry.Name()
		if strings.HasSuffix(name, ".yml") || strings.HasSuffix(name, ".yaml") {
			paths = append(paths, filepath.Join(workflowRoot, name))
		}
	}
	sort.Strings(paths)
	return paths
}

func readWorkflowLines(t *testing.T, workflowPath string) []string {
	t.Helper()
	content, err := os.ReadFile(workflowPath)
	if err != nil {
		t.Fatalf("failed to read workflow %s: %v", workflowPath, err)
	}
	return strings.Split(string(content), "\n")
}

func parseUsesAction(line string) (string, bool) {
	trimmedLine := strings.TrimSpace(stripYamlComment(line))
	if !strings.HasPrefix(trimmedLine, "uses:") {
		return "", false
	}
	actionRef := strings.TrimSpace(strings.TrimPrefix(trimmedLine, "uses:"))
	actionRef = strings.Trim(actionRef, `"'`)
	return actionRef, actionRef != ""
}

func workflowRunsOnPullRequest(lines []string) bool {
	for _, line := range lines {
		trimmedLine := strings.TrimSpace(stripYamlComment(line))
		if strings.HasPrefix(trimmedLine, "pull_request:") || strings.HasPrefix(trimmedLine, "pull_request_target:") {
			return true
		}
	}
	return false
}

func stripYamlComment(line string) string {
	beforeComment, _, _ := strings.Cut(line, "#")
	return beforeComment
}

func isLocalAction(actionRef string) bool {
	return strings.HasPrefix(actionRef, "./") || strings.HasPrefix(actionRef, "docker://")
}

func actionReference(actionRef string) string {
	atIndex := strings.LastIndex(actionRef, "@")
	if atIndex < 0 {
		return ""
	}
	return actionRef[atIndex+1:]
}

func actionRepository(actionRef string) string {
	atIndex := strings.LastIndex(actionRef, "@")
	if atIndex < 0 {
		return ""
	}
	pathParts := strings.Split(actionRef[:atIndex], "/")
	if len(pathParts) < 2 {
		return ""
	}
	return pathParts[0] + "/" + pathParts[1]
}

func stepContains(lines []string, lineIndex int, expectedLine string) bool {
	startIndex := stepStartIndex(lines, lineIndex)
	for index := startIndex; index < len(lines); index++ {
		if index > startIndex && isStepStart(lines[index]) {
			return false
		}
		if strings.TrimSpace(stripYamlComment(lines[index])) == expectedLine {
			return true
		}
	}
	return false
}

func stepStartIndex(lines []string, lineIndex int) int {
	for index := lineIndex; index >= 0; index-- {
		if isStepStart(lines[index]) {
			return index
		}
	}
	return lineIndex
}

func isStepStart(line string) bool {
	return strings.HasPrefix(strings.TrimSpace(stripYamlComment(line)), "- name:")
}

func workflowViolation(repositoryRoot string, workflowPath string, lineIndex int, actionRef string, message string) string {
	relativePath, err := filepath.Rel(repositoryRoot, workflowPath)
	if err != nil {
		relativePath = workflowPath
	}
	return fmt.Sprintf("%s:%d uses %q; %s", filepath.ToSlash(relativePath), lineIndex+1, actionRef, message)
}
