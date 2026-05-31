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

// Tests that unnamed action steps are still parsed as remote action uses.
func TestParseUsesActionAcceptsUnnamedSteps(t *testing.T) {
	actionRef, ok := parseUsesAction("      - uses: actions/checkout@v6")
	if !ok {
		t.Fatal("expected unnamed uses step to be parsed")
	}
	if actionRef != "actions/checkout@v6" {
		t.Fatalf("unexpected action ref: %s", actionRef)
	}
}

// Tests that step-local checks do not read settings from neighboring unnamed steps.
func TestStepContainsStopsAtUnnamedStepBoundaries(t *testing.T) {
	lines := []string{
		"      - uses: actions/setup-go@4a3601121dd01d1626a1e23e37211e3254c1c06c",
		"        with:",
		"          go-version-file: cli/.go-version",
		"      - uses: actions/cache@27d5ce7f107fe9357f9df03efb73ab90386fccae",
		"        with:",
		"          cache: false",
	}
	if stepContains(lines, 0, "cache: false") {
		t.Fatal("expected cache setting in a neighboring unnamed step to be ignored")
	}
}

// Tests that inline pull request triggers are recognized without matching unrelated triggers.
func TestWorkflowRunsOnPullRequestDetectsInlineOnSyntax(t *testing.T) {
	testCases := []struct {
		name     string
		lines    []string
		expected bool
	}{
		{
			name:     "single inline pull_request",
			lines:    []string{"on: pull_request"},
			expected: true,
		},
		{
			name:     "inline trigger list",
			lines:    []string{"on: [push, pull_request]"},
			expected: true,
		},
		{
			name:     "inline trigger map",
			lines:    []string{"on: {pull_request: {}}"},
			expected: true,
		},
		{
			name:     "single inline pull_request_target",
			lines:    []string{"on: pull_request_target"},
			expected: true,
		},
		{
			name:     "block trigger list",
			lines:    []string{"on:", "  - push", "  - pull_request"},
			expected: true,
		},
		{
			name:     "block trigger map",
			lines:    []string{"on:", "  push:", "  pull_request:", "    branches: [main]"},
			expected: true,
		},
		{
			name:     "unrelated pull request trigger",
			lines:    []string{"on: pull_request_review"},
			expected: false,
		},
		{
			name:     "unrelated inline trigger list",
			lines:    []string{"on: [push, pull_request_review]"},
			expected: false,
		},
		{
			name:     "unrelated inline trigger map nested value",
			lines:    []string{"on: {workflow_run: {workflows: [pull_request]}}"},
			expected: false,
		},
		{
			name:     "unrelated block trigger nested value",
			lines:    []string{"on:", "  workflow_run:", "    workflows: [pull_request]"},
			expected: false,
		},
	}
	for _, testCase := range testCases {
		t.Run(testCase.name, func(t *testing.T) {
			if workflowRunsOnPullRequest(testCase.lines) != testCase.expected {
				t.Fatalf("pull request trigger detection mismatch for %v", testCase.lines)
			}
		})
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
	trimmedLine = strings.TrimPrefix(trimmedLine, "- ")
	if !strings.HasPrefix(trimmedLine, "uses:") {
		return "", false
	}
	actionRef := strings.TrimSpace(strings.TrimPrefix(trimmedLine, "uses:"))
	actionRef = strings.Trim(actionRef, `"'`)
	return actionRef, actionRef != ""
}

func workflowRunsOnPullRequest(lines []string) bool {
	inOnBlock := false
	onChildIndent := -1
	for _, line := range lines {
		lineWithoutComment := stripYamlComment(line)
		trimmedLine := strings.TrimSpace(lineWithoutComment)
		if trimmedLine == "" {
			continue
		}
		if strings.HasPrefix(trimmedLine, "on:") {
			onValue := strings.TrimSpace(strings.TrimPrefix(trimmedLine, "on:"))
			if onValue != "" {
				return workflowInlineTriggerContainsPullRequest(onValue)
			}
			inOnBlock = true
			onChildIndent = -1
			continue
		}
		if !inOnBlock {
			continue
		}
		if isTopLevelYamlKey(lineWithoutComment) {
			return false
		}
		lineIndent := leadingWhitespaceCount(lineWithoutComment)
		if onChildIndent < 0 {
			onChildIndent = lineIndent
		}
		if lineIndent != onChildIndent {
			continue
		}
		if workflowBlockTriggerLineContainsPullRequest(trimmedLine) {
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
	trimmedLine := strings.TrimSpace(stripYamlComment(line))
	if !strings.HasPrefix(trimmedLine, "- ") {
		return false
	}
	stepContent := strings.TrimSpace(strings.TrimPrefix(trimmedLine, "- "))
	stepKey, _, hasStepKey := strings.Cut(stepContent, ":")
	return hasStepKey && stepKey != "" && !strings.ContainsAny(stepKey, " []{}")
}

func workflowTriggerListContainsPullRequest(value string) bool {
	value = strings.TrimSpace(value)
	value = strings.TrimPrefix(value, "- ")
	normalizedValue := strings.NewReplacer("[", " ", "]", " ", "{", " ", "}", " ", ",", " ", ":", " ", `"`, " ", `'`, " ").Replace(value)
	for _, token := range strings.Fields(normalizedValue) {
		if token == "pull_request" || token == "pull_request_target" {
			return true
		}
	}
	return false
}

func workflowInlineTriggerContainsPullRequest(value string) bool {
	value = strings.TrimSpace(value)
	if strings.HasPrefix(value, "{") {
		return workflowInlineMapContainsPullRequest(value)
	}
	return workflowTriggerListContainsPullRequest(value)
}

func workflowInlineMapContainsPullRequest(value string) bool {
	content := strings.TrimSpace(strings.TrimSuffix(strings.TrimPrefix(value, "{"), "}"))
	for _, entry := range splitTopLevelCommaEntries(content) {
		key := topLevelMapKey(entry)
		if key == "pull_request" || key == "pull_request_target" {
			return true
		}
	}
	return false
}

func splitTopLevelCommaEntries(value string) []string {
	entries := []string{}
	startIndex := 0
	nestingDepth := 0
	for index, char := range value {
		switch char {
		case '{', '[':
			nestingDepth++
		case '}', ']':
			if nestingDepth > 0 {
				nestingDepth--
			}
		case ',':
			if nestingDepth == 0 {
				entries = append(entries, value[startIndex:index])
				startIndex = index + 1
			}
		}
	}
	entries = append(entries, value[startIndex:])
	return entries
}

func topLevelMapKey(entry string) string {
	nestingDepth := 0
	for index, char := range entry {
		switch char {
		case '{', '[':
			nestingDepth++
		case '}', ']':
			if nestingDepth > 0 {
				nestingDepth--
			}
		case ':':
			if nestingDepth == 0 {
				return trimWorkflowToken(entry[:index])
			}
		}
	}
	return ""
}

func workflowBlockTriggerLineContainsPullRequest(value string) bool {
	value = strings.TrimSpace(strings.TrimPrefix(strings.TrimSpace(value), "- "))
	key, _, hasKey := strings.Cut(value, ":")
	if hasKey {
		value = key
	}
	value = trimWorkflowToken(value)
	return value == "pull_request" || value == "pull_request_target"
}

func trimWorkflowToken(value string) string {
	return strings.Trim(strings.TrimSpace(value), `"'`)
}

func isTopLevelYamlKey(line string) bool {
	trimmedLine := strings.TrimSpace(line)
	if trimmedLine == "" || strings.HasPrefix(line, " ") || strings.HasPrefix(line, "\t") {
		return false
	}
	key, _, hasKey := strings.Cut(trimmedLine, ":")
	return hasKey && key != ""
}

func leadingWhitespaceCount(value string) int {
	trimmedValue := strings.TrimLeft(value, " \t")
	return len(value) - len(trimmedValue)
}

func workflowViolation(repositoryRoot string, workflowPath string, lineIndex int, actionRef string, message string) string {
	relativePath, err := filepath.Rel(repositoryRoot, workflowPath)
	if err != nil {
		relativePath = workflowPath
	}
	return fmt.Sprintf("%s:%d uses %q; %s", filepath.ToSlash(relativePath), lineIndex+1, actionRef, message)
}
