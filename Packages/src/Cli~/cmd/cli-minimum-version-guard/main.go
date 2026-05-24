package main

import (
	"encoding/json"
	"fmt"
	"os"
	"os/exec"
	"path"
	"regexp"
	"strings"

	"github.com/hatayama/unity-cli-loop/Packages/src/Cli/internal/version"
)

const (
	minimumVersionFile = "Packages/src/Editor/Domain/CliConstants.cs"
	contractFile       = "Packages/src/Cli~/contract.json"
	decisionFile       = ".github/cli-minimum-version-decision.json"
)

var minimumVersionPattern = regexp.MustCompile(`MINIMUM_REQUIRED_CLI_VERSION\s*=\s*"([^"]+)"`)

type cliContract struct {
	CLIVersion string `json:"cliVersion"`
}

func main() {
	repositoryRoot, err := gitOutput("", "rev-parse", "--show-toplevel")
	if err != nil {
		_, _ = fmt.Fprintln(os.Stderr, err)
		os.Exit(1)
	}
	repositoryRoot = strings.TrimSpace(repositoryRoot)
	if err := check(repositoryRoot); err != nil {
		_, _ = fmt.Fprintln(os.Stderr, err)
		os.Exit(1)
	}
	_, _ = fmt.Fprintln(os.Stdout, "CLI minimum-version compatibility guard passed.")
}

func check(repositoryRoot string) error {
	minimumVersion, err := minimumRequiredCLIVersion(repositoryRoot)
	if err != nil {
		return err
	}
	latestVersion, err := latestCLIContractVersion(repositoryRoot)
	if err != nil {
		return err
	}
	comparison, ok := version.Compare(minimumVersion, latestVersion)
	if !ok {
		return fmt.Errorf("invalid CLI version comparison: %s vs %s", minimumVersion, latestVersion)
	}
	if comparison > 0 {
		return fmt.Errorf("MINIMUM_REQUIRED_CLI_VERSION (%s) must not be greater than CLI contract cliVersion (%s)", minimumVersion, latestVersion)
	}

	baseRef := resolveBaseRef(repositoryRoot)
	changedFiles, err := changedFiles(repositoryRoot, baseRef)
	if err != nil {
		return err
	}

	minimumVersionChanged := false
	if baseRef != "" {
		baseMinimumVersion, err := baseMinimumRequiredCLIVersion(repositoryRoot, baseRef)
		if err != nil {
			return err
		}
		minimumVersionChanged = baseMinimumVersion != "" && baseMinimumVersion != minimumVersion
	}

	if _, err := os.Stat(path.Join(repositoryRoot, decisionFile)); err == nil {
		if err := validateKeepDecision(repositoryRoot, minimumVersion); err != nil {
			return err
		}
	}

	sensitiveChange, decisionChanged := changedFileState(changedFiles)
	if sensitiveChange && !minimumVersionChanged {
		if !decisionChanged {
			return fmt.Errorf("compatibility-sensitive CLI or Editor/CLI files changed without updating MINIMUM_REQUIRED_CLI_VERSION or %s", decisionFile)
		}
		return validateKeepDecision(repositoryRoot, minimumVersion)
	}

	return nil
}

func minimumRequiredCLIVersion(repositoryRoot string) (string, error) {
	content, err := os.ReadFile(path.Join(repositoryRoot, minimumVersionFile))
	if err != nil {
		return "", err
	}
	match := minimumVersionPattern.FindStringSubmatch(string(content))
	if len(match) != 2 {
		return "", fmt.Errorf("missing MINIMUM_REQUIRED_CLI_VERSION in %s", minimumVersionFile)
	}
	return match[1], nil
}

func latestCLIContractVersion(repositoryRoot string) (string, error) {
	content, err := os.ReadFile(path.Join(repositoryRoot, contractFile))
	if err != nil {
		return "", err
	}
	var contract cliContract
	if err := json.Unmarshal(content, &contract); err != nil {
		return "", err
	}
	if strings.TrimSpace(contract.CLIVersion) == "" {
		return "", fmt.Errorf("missing cliVersion in %s", contractFile)
	}
	return contract.CLIVersion, nil
}

func resolveBaseRef(repositoryRoot string) string {
	if baseRef := strings.TrimSpace(os.Getenv("CLI_MINIMUM_VERSION_BASE_REF")); baseRef != "" {
		return baseRef
	}
	if baseRef := strings.TrimSpace(os.Getenv("GITHUB_BASE_REF")); baseRef != "" {
		return "origin/" + baseRef
	}
	if _, err := gitOutput(repositoryRoot, "rev-parse", "--verify", "HEAD^"); err == nil {
		return "HEAD^"
	}
	return ""
}

func changedFiles(repositoryRoot string, baseRef string) ([]string, error) {
	if baseRef == "" {
		return nil, nil
	}
	output, err := gitOutput(repositoryRoot, "diff", "--name-only", baseRef+"...HEAD", "--")
	if err != nil {
		return nil, err
	}
	return strings.Split(strings.TrimSpace(output), "\n"), nil
}

func baseMinimumRequiredCLIVersion(repositoryRoot string, baseRef string) (string, error) {
	output, err := gitOutput(repositoryRoot, "show", baseRef+":"+minimumVersionFile)
	if err != nil {
		return "", err
	}
	match := minimumVersionPattern.FindStringSubmatch(output)
	if len(match) != 2 {
		return "", nil
	}
	return match[1], nil
}

func isCompatibilitySensitivePath(file string) bool {
	switch file {
	case contractFile,
		"Packages/src/Cli~/layout-contract.json",
		"scripts/install.sh",
		"scripts/install.ps1",
		"Packages/src/Editor/Application/CliSetupApplicationService.cs",
		"Packages/src/Editor/CompositionRoot/UnityCliLoopFirstPartyServerLifecycleBinding.cs":
		return true
	}
	if path.Dir(file) == "Packages/src/Cli~" && strings.HasSuffix(path.Base(file), ".go") {
		return true
	}
	for _, prefix := range []string{
		"Packages/src/Cli~/cmd/uloop/",
		"Packages/src/Cli~/internal/",
		"Packages/src/Editor/Infrastructure/Api/",
		"Packages/src/Editor/Infrastructure/CLI/",
	} {
		if strings.HasPrefix(file, prefix) {
			return true
		}
	}
	return false
}

func changedFileState(files []string) (bool, bool) {
	sensitiveChange := false
	decisionChanged := false
	for _, file := range files {
		if isCompatibilitySensitivePath(file) {
			sensitiveChange = true
		}
		if file == decisionFile {
			decisionChanged = true
		}
	}
	return sensitiveChange, decisionChanged
}

func validateKeepDecision(repositoryRoot string, minimumVersion string) error {
	content, err := os.ReadFile(path.Join(repositoryRoot, decisionFile))
	if err != nil {
		return fmt.Errorf("missing %s: add a keep decision or update MINIMUM_REQUIRED_CLI_VERSION", decisionFile)
	}
	var decision map[string]string
	if err := json.Unmarshal(content, &decision); err != nil {
		return err
	}
	if decision["minimumRequiredCliVersion"] != minimumVersion {
		return fmt.Errorf("%s must document the current minimumRequiredCliVersion (%s)", decisionFile, minimumVersion)
	}
	if decision["decision"] != "keep" {
		return fmt.Errorf("%s decision must be \"keep\"", decisionFile)
	}
	if strings.TrimSpace(decision["reason"]) == "" {
		return fmt.Errorf("%s reason must not be empty", decisionFile)
	}
	return nil
}

func gitOutput(repositoryRoot string, args ...string) (string, error) {
	command := exec.Command("git", args...)
	if repositoryRoot != "" {
		command.Dir = repositoryRoot
	}
	output, err := command.CombinedOutput()
	if err != nil {
		return "", fmt.Errorf("git %s failed: %s", strings.Join(args, " "), strings.TrimSpace(string(output)))
	}
	return string(output), nil
}
