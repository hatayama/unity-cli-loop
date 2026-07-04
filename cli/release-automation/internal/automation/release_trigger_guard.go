package automation

import (
	"context"
	"fmt"
	"io"
	"path"
	"sort"
	"strings"
)

// releaseTriggerRule ties out-of-package release inputs to the release package
// roots that must change alongside them.
type releaseTriggerRule struct {
	inputDescription string
	matchesInput     func(file string) bool
	triggerRoots     []string
}

// release-please attributes a commit to a component only when the commit
// touches that package root, so inputs living outside every package root
// (the common module, installer assets) need a companion change inside each
// consuming package root or the change never reaches a release.
//
// These rules are re-encoded as shell in scripts/stamp-release-inputs.sh
// (list_shared_common_inputs / list_dispatcher_only_common_inputs /
// list_installer_inputs); update both together. scripts/test-stamp-release-inputs.sh
// covers the same shared, dispatcher-only, installer, and ignored input cases.
var releaseTriggerRules = []releaseTriggerRule{
	{
		inputDescription: "common module sources shared by dispatcher and project runner",
		matchesInput:     isSharedCommonModuleInput,
		triggerRoots:     []string{"cli/dispatcher/", "cli/project-runner/"},
	},
	{
		inputDescription: "common module sources used only by dispatcher",
		matchesInput:     isDispatcherOnlyCommonModuleInput,
		triggerRoots:     []string{"cli/dispatcher/"},
	},
	{
		inputDescription: "installer scripts shipped as dispatcher release assets",
		matchesInput:     isDispatcherInstallerScript,
		triggerRoots:     []string{"cli/dispatcher/"},
	},
}

type ReleaseTriggerGuardConfig struct {
	BaseRef string
	HeadRef string
}

type ReleaseTriggerGuardResult struct {
	Violations []ReleaseTriggerViolation
}

type ReleaseTriggerViolation struct {
	InputDescription    string
	ChangedInputs       []string
	MissingTriggerRoots []string
}

func RunReleaseTriggerGuard(
	ctx context.Context,
	stdout io.Writer,
	stderr io.Writer,
	config ReleaseTriggerGuardConfig,
) int {
	result, err := AnalyzeReleaseTriggerGuardForRefs(ctx, config)
	if err != nil {
		writeReleaseTriggerLine(stderr, err)
		return 1
	}
	if len(result.Violations) > 0 {
		writeReleaseTriggerLine(stderr, FormatReleaseTriggerWarning(result))
		return 1
	}

	writeReleaseTriggerLine(stdout, "Release trigger guard passed.")
	return 0
}

func AnalyzeReleaseTriggerGuardForRefs(
	ctx context.Context,
	config ReleaseTriggerGuardConfig,
) (ReleaseTriggerGuardResult, error) {
	if config.BaseRef == "" {
		return ReleaseTriggerGuardResult{}, fmt.Errorf("--base is required")
	}
	if config.HeadRef == "" {
		config.HeadRef = "HEAD"
	}

	repoRoot, err := gitRepoRoot(ctx)
	if err != nil {
		return ReleaseTriggerGuardResult{}, fmt.Errorf("failed to resolve git repository root: %w", err)
	}

	changedFiles, err := gitChangedFiles(ctx, repoRoot, config.BaseRef, config.HeadRef)
	if err != nil {
		return ReleaseTriggerGuardResult{}, fmt.Errorf("failed to inspect changed files: %w", err)
	}

	return AnalyzeReleaseTriggerGuard(changedFiles), nil
}

func AnalyzeReleaseTriggerGuard(changedFiles []string) ReleaseTriggerGuardResult {
	normalizedFiles := []string{}
	for _, changedFile := range changedFiles {
		normalizedFile := normalizeChangedFilePath(changedFile)
		if normalizedFile == "" {
			continue
		}
		normalizedFiles = append(normalizedFiles, normalizedFile)
	}

	violations := []ReleaseTriggerViolation{}
	for _, rule := range releaseTriggerRules {
		changedInputs := []string{}
		for _, normalizedFile := range normalizedFiles {
			if rule.matchesInput(normalizedFile) {
				changedInputs = append(changedInputs, normalizedFile)
			}
		}
		if len(changedInputs) == 0 {
			continue
		}
		sort.Strings(changedInputs)

		missingTriggerRoots := []string{}
		for _, triggerRoot := range rule.triggerRoots {
			if !anyFileUnderRoot(normalizedFiles, triggerRoot) {
				missingTriggerRoots = append(missingTriggerRoots, triggerRoot)
			}
		}
		if len(missingTriggerRoots) == 0 {
			continue
		}

		violations = append(violations, ReleaseTriggerViolation{
			InputDescription:    rule.inputDescription,
			ChangedInputs:       changedInputs,
			MissingTriggerRoots: missingTriggerRoots,
		})
	}

	return ReleaseTriggerGuardResult{Violations: violations}
}

var sharedCommonPackageRoots = []string{
	// Keep this whitelist aligned with `go list -deps ./...` for the dispatcher
	// and project-runner modules. Test helpers such as cli/common/clitest are
	// intentionally excluded because they never ship in release binaries.
	"cli/common/clicontract/",
	"cli/common/clicore/",
	"cli/common/errors/",
	"cli/common/project/",
	"cli/common/skillscan/",
	"cli/common/tools/",
	"cli/common/unityipc/",
	"cli/common/unityprocess/",
}

var dispatcherOnlyCommonPackageRoots = []string{
	// This package is imported by the dispatcher update path, not by the
	// project runner.
	"cli/common/version/",
}

func isSharedCommonModuleInput(file string) bool {
	if file == "cli/common/go.mod" || file == "cli/common/go.sum" {
		return true
	}
	return isCommonGoSourceUnderPackageRoots(file, sharedCommonPackageRoots)
}

func isDispatcherOnlyCommonModuleInput(file string) bool {
	return isCommonGoSourceUnderPackageRoots(file, dispatcherOnlyCommonPackageRoots)
}

func isCommonGoSourceUnderPackageRoots(file string, packageRoots []string) bool {
	if !strings.HasPrefix(file, "cli/common/") {
		return false
	}
	// JSON files under common (contract.json, default-tools.json) are
	// release-please stamp targets rather than binary inputs, and test files
	// never ship, so only code and embedded runtime scripts count as release inputs.
	if strings.HasSuffix(file, "_test.go") || (!strings.HasSuffix(file, ".go") && !strings.HasSuffix(file, ".ps1")) {
		return false
	}
	for _, packageRoot := range packageRoots {
		if strings.HasPrefix(file, packageRoot) {
			return true
		}
	}
	return false
}

func isDispatcherInstallerScript(file string) bool {
	return file == "scripts/install.sh" || file == "scripts/install.ps1"
}

func anyFileUnderRoot(files []string, root string) bool {
	for _, file := range files {
		if strings.HasPrefix(file, root) {
			return true
		}
	}
	return false
}

func normalizeChangedFilePath(value string) string {
	normalizedValue := strings.ReplaceAll(strings.TrimSpace(value), "\\", "/")
	normalizedValue = strings.TrimPrefix(normalizedValue, "./")
	if normalizedValue == "" {
		return ""
	}
	return path.Clean(normalizedValue)
}

func FormatReleaseTriggerWarning(result ReleaseTriggerGuardResult) string {
	builder := strings.Builder{}
	builder.WriteString("Out-of-package release inputs changed without matching release triggers.\n")
	for _, violation := range result.Violations {
		builder.WriteString("\n")
		builder.WriteString(violation.InputDescription)
		builder.WriteString(" changed:\n")
		for _, changedInput := range violation.ChangedInputs {
			builder.WriteString("- `")
			builder.WriteString(changedInput)
			builder.WriteString("`\n")
		}
		builder.WriteString("\nMissing release trigger updates under:\n")
		for _, missingRoot := range violation.MissingTriggerRoots {
			builder.WriteString("- `")
			builder.WriteString(missingRoot)
			builder.WriteString("`\n")
		}
	}
	builder.WriteString("\nRun `scripts/stamp-release-inputs.sh` and commit the updated stamp files so this change reaches every affected release.")
	return builder.String()
}

func writeReleaseTriggerLine(writer io.Writer, values ...any) {
	_, _ = fmt.Fprintln(writer, values...)
}
