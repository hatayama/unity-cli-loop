package automation

import (
	"bytes"
	"context"
	"encoding/json"
	"fmt"
	"io"
	"os"
	"os/exec"
	"path/filepath"
	"strings"
)

const (
	minimumVersionWarningFile   = "Packages/src/Editor/Domain/CliConstants.cs"
	minimumVersionWarningMarker = "<!-- uloop-cli-minimum-version-warning -->"
	minimumVersionWarningBody   = minimumVersionWarningMarker + `
Warning: Go CLI files changed, but ` + "`MINIMUM_REQUIRED_CLI_VERSION`" + ` was not updated.

Please confirm whether the Unity package can still accept older CLI versions. If the package now depends on new CLI behavior, update ` + "`Packages/src/Editor/Domain/CliConstants.cs`" + `.
`
	resolvedMinimumVersionWarningBody = minimumVersionWarningMarker + `
Resolved: this PR no longer has Go CLI changes without a ` + "`MINIMUM_REQUIRED_CLI_VERSION`" + ` update.
`
	goCliPackageRoot = "Packages/src/Cli~/"
)

type minimumVersionWarningConfig struct {
	repositoryRoot string
	pullRequest    string
	repository     string
	baseRef        string
	headRef        string
	failOnWarning  bool
}

func RunMinimumVersionWarning(ctx context.Context, stdout io.Writer, stderr io.Writer) int {
	config, err := minimumVersionWarningConfigFromEnvironment()
	if err != nil {
		writeMinimumVersionWarningLine(stderr, err)
		return 1
	}
	if config.pullRequest == "" && !config.failOnWarning {
		writeMinimumVersionWarningLine(stdout, "Skipping CLI minimum version comment because no PR number was provided.")
		return 0
	}

	if config.baseRef == "" {
		writeMinimumVersionWarningLine(stdout, "Skipping CLI minimum version comment because no base ref was provided.")
		return 0
	}

	changedFiles, err := minimumVersionWarningChangedFiles(ctx, config)
	if err != nil {
		writeMinimumVersionWarningLine(stderr, err)
		return 1
	}

	requiresComment := minimumVersionWarningRequiresComment(changedFiles)
	if config.failOnWarning {
		if requiresComment {
			writeMinimumVersionWarningLine(stderr, strings.TrimSpace(minimumVersionWarningBody))
			return 1
		}
		return 0
	}

	repository, err := resolveMinimumVersionWarningRepository(ctx, config)
	if err != nil {
		writeMinimumVersionWarningLine(stderr, err)
		return 1
	}
	config.repository = repository

	if requiresComment {
		message, err := upsertMinimumVersionWarningComment(ctx, config, minimumVersionWarningBody)
		if err != nil {
			writeMinimumVersionWarningLine(stderr, err)
			return 1
		}
		writeMinimumVersionWarningLine(stdout, message)
		return 0
	}

	resolved, err := resolveMinimumVersionWarningComment(ctx, config)
	if err != nil {
		writeMinimumVersionWarningLine(stderr, err)
		return 1
	}
	if resolved {
		writeMinimumVersionWarningLine(stdout, "Resolved CLI minimum version comment.")
	}
	return 0
}

func minimumVersionWarningConfigFromEnvironment() (minimumVersionWarningConfig, error) {
	repositoryRoot := os.Getenv("ULOOP_REPOSITORY_ROOT")
	if repositoryRoot == "" {
		workingDirectory, err := os.Getwd()
		if err != nil {
			return minimumVersionWarningConfig{}, fmt.Errorf("failed to resolve repository root: %w", err)
		}
		repositoryRoot = workingDirectory
	}

	baseRef := os.Getenv("CLI_MINIMUM_VERSION_BASE_REF")
	if baseRef == "" && os.Getenv("GITHUB_BASE_REF") != "" {
		baseRef = "origin/" + os.Getenv("GITHUB_BASE_REF")
	}

	headRef := os.Getenv("CLI_MINIMUM_VERSION_HEAD_REF")
	if headRef == "" {
		headRef = "HEAD"
	}

	return minimumVersionWarningConfig{
		repositoryRoot: repositoryRoot,
		pullRequest:    os.Getenv("PR_NUMBER"),
		repository:     os.Getenv("GITHUB_REPOSITORY"),
		baseRef:        baseRef,
		headRef:        headRef,
		failOnWarning:  os.Getenv("CLI_MINIMUM_VERSION_FAIL_ON_WARNING") == "true",
	}, nil
}

func resolveMinimumVersionWarningRepository(ctx context.Context, config minimumVersionWarningConfig) (string, error) {
	if config.repository != "" {
		return config.repository, nil
	}
	output, err := runMinimumVersionWarningOutput(ctx, config.repositoryRoot, "gh", "repo", "view", "--json", "nameWithOwner", "--jq", ".nameWithOwner")
	if err != nil {
		return "", err
	}
	return strings.TrimSpace(output), nil
}

func minimumVersionWarningChangedFiles(ctx context.Context, config minimumVersionWarningConfig) ([]string, error) {
	output, err := runMinimumVersionWarningOutput(ctx, config.repositoryRoot, "git", "-C", config.repositoryRoot, "diff", "--name-only", config.baseRef+"..."+config.headRef, "--")
	if err != nil {
		return nil, err
	}

	changedFiles := []string{}
	for _, line := range strings.Split(output, "\n") {
		changedFile := strings.TrimSpace(line)
		if changedFile != "" {
			changedFiles = append(changedFiles, changedFile)
		}
	}
	return changedFiles, nil
}

func minimumVersionWarningRequiresComment(changedFiles []string) bool {
	hasGoCliChange := false
	for _, changedFile := range changedFiles {
		if changedFile == minimumVersionWarningFile {
			return false
		}
		hasGoCliChange = hasGoCliChange || minimumVersionWarningIsGoCliFile(changedFile)
	}
	return hasGoCliChange
}

func upsertMinimumVersionWarningComment(ctx context.Context, config minimumVersionWarningConfig, body string) (string, error) {
	commentID, err := existingMinimumVersionWarningComment(ctx, config)
	if err != nil {
		return "", err
	}
	bodyFile, cleanup, err := writeMinimumVersionWarningBodyFile(body)
	if err != nil {
		return "", err
	}
	defer cleanup()

	if commentID != "" {
		_, err := runMinimumVersionWarningOutput(ctx, config.repositoryRoot, "gh", "api", "--method", "PATCH", "repos/"+config.repository+"/issues/comments/"+commentID, "--input", bodyFile)
		return "Updated CLI minimum version comment.", err
	}
	_, err = runMinimumVersionWarningOutput(ctx, config.repositoryRoot, "gh", "api", "--method", "POST", "repos/"+config.repository+"/issues/"+config.pullRequest+"/comments", "--input", bodyFile)
	return "Posted CLI minimum version comment.", err
}

func resolveMinimumVersionWarningComment(ctx context.Context, config minimumVersionWarningConfig) (bool, error) {
	commentID, err := existingMinimumVersionWarningComment(ctx, config)
	if err != nil {
		return false, err
	}
	if commentID == "" {
		return false, nil
	}

	bodyFile, cleanup, err := writeMinimumVersionWarningBodyFile(resolvedMinimumVersionWarningBody)
	if err != nil {
		return false, err
	}
	defer cleanup()

	_, err = runMinimumVersionWarningOutput(ctx, config.repositoryRoot, "gh", "api", "--method", "PATCH", "repos/"+config.repository+"/issues/comments/"+commentID, "--input", bodyFile)
	return err == nil, err
}

func existingMinimumVersionWarningComment(ctx context.Context, config minimumVersionWarningConfig) (string, error) {
	output, err := runMinimumVersionWarningOutput(
		ctx,
		config.repositoryRoot,
		"gh",
		"api",
		"--paginate",
		"repos/"+config.repository+"/issues/"+config.pullRequest+"/comments",
		"--jq",
		".[] | select(.body | contains(\""+minimumVersionWarningMarker+"\")) | .id",
	)
	if err != nil {
		return "", err
	}

	lines := strings.Split(strings.TrimSpace(output), "\n")
	for index := len(lines) - 1; index >= 0; index-- {
		line := strings.TrimSpace(lines[index])
		if line != "" {
			return line, nil
		}
	}
	return "", nil
}

func runMinimumVersionWarningOutput(ctx context.Context, workDir string, name string, args ...string) (string, error) {
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

func minimumVersionWarningIsGoCliFile(changedFile string) bool {
	if changedFile == goCliPackageRoot+"CHANGELOG.md" ||
		strings.HasPrefix(changedFile, goCliPackageRoot+"dist/") ||
		strings.HasPrefix(changedFile, goCliPackageRoot+"cmd/comment-cli-minimum-version-warning/") ||
		strings.HasPrefix(changedFile, goCliPackageRoot+"cmd/dispatch-release-please-pr-checks/") ||
		strings.HasPrefix(changedFile, goCliPackageRoot+"internal/automation/") ||
		strings.HasSuffix(changedFile, "_test.go") {
		return false
	}
	if strings.HasPrefix(changedFile, goCliPackageRoot+"cmd/") || strings.HasPrefix(changedFile, goCliPackageRoot+"internal/") {
		return true
	}
	switch changedFile {
	case goCliPackageRoot + "go.mod",
		goCliPackageRoot + "go.sum",
		goCliPackageRoot + "contract.json",
		goCliPackageRoot + "layout-contract.json":
		return true
	default:
		relativePath := strings.TrimPrefix(changedFile, goCliPackageRoot)
		return relativePath != changedFile && strings.HasSuffix(relativePath, ".go") && !strings.Contains(relativePath, "/")
	}
}

func writeMinimumVersionWarningBodyFile(body string) (string, func(), error) {
	bodyFile, err := os.CreateTemp("", "uloop-minimum-version-warning-*.json")
	if err != nil {
		return "", func() {}, fmt.Errorf("failed to create comment body file: %w", err)
	}
	cleanup := func() { _ = os.Remove(bodyFile.Name()) }

	err = json.NewEncoder(bodyFile).Encode(struct {
		Body string `json:"body"`
	}{Body: body})
	closeErr := bodyFile.Close()
	if err != nil || closeErr != nil {
		cleanup()
		if err != nil {
			return "", func() {}, fmt.Errorf("failed to encode comment body: %w", err)
		}
		return "", func() {}, fmt.Errorf("failed to close comment body file: %w", closeErr)
	}
	return bodyFile.Name(), cleanup, nil
}

func writeMinimumVersionWarningLine(writer io.Writer, values ...any) {
	// CI status output failures cannot be recovered after the command outcome is known.
	_, _ = fmt.Fprintln(writer, values...)
}
