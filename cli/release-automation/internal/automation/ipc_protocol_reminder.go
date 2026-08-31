package automation

import (
	"context"
	"fmt"
	"io"
	"os"
	"os/exec"
	"sort"
	"strings"
)

var ipcProtocolReminderPatterns = []string{
	"cli/common/clicontract/contract.json",
	"cli/common/clicontract/contract.go",
	"layout-contract.json",
	"cli/common/unityipc/**",
	"cli/common/tools/**",
	"Packages/src/Editor/CompositionRoot/UnityCliLoopFirstPartyServerLifecycleBinding.cs",
	"Packages/src/Editor/Domain/CliConstants.cs",
	"Packages/src/Editor/Infrastructure/Api/**",
	"Packages/src/Editor/ToolContracts/UnityCliLoopConstants.cs",
	"Packages/src/Editor/ToolContracts/UnityCliLoopToolSchema.cs",
	"Packages/src/Editor/ToolContracts/UnityCliLoopToolParameterSchemaGenerator.cs",
}

var ipcProtocolDeclarationPaths = []string{
	"cli/common/clicontract/contract.json",
	"Packages/src/Editor/Domain/CliConstants.cs",
}

type IPCProtocolReminderConfig struct {
	BaseRef         string
	HeadRef         string
	StepSummaryPath string
}

type IPCProtocolReminderResult struct {
	ChangedIPCFiles          []string
	ChangedProtocolFiles     []string
	NeedsProtocolBumpReview  bool
	HasProtocolFileChange    bool
	HasIPCContractFileChange bool
}

func RunIPCProtocolReminder(ctx context.Context, stdout io.Writer, stderr io.Writer, config IPCProtocolReminderConfig) int {
	if config.BaseRef == "" {
		writeIPCProtocolReminderLine(stderr, "--base is required")
		return 1
	}
	if config.HeadRef == "" {
		config.HeadRef = "HEAD"
	}

	repoRoot, err := gitRepoRoot(ctx)
	if err != nil {
		writeIPCProtocolReminderLine(stderr, fmt.Sprintf("failed to resolve git repository root: %v", err))
		return 1
	}

	changedFiles, err := gitChangedFiles(ctx, repoRoot, config.BaseRef, config.HeadRef)
	if err != nil {
		writeIPCProtocolReminderLine(stderr, fmt.Sprintf("failed to inspect changed files: %v", err))
		return 1
	}

	result := AnalyzeIPCProtocolReminder(changedFiles)
	message := FormatIPCProtocolReminder(result)
	writeIPCProtocolReminderLine(stdout, message)

	if result.NeedsProtocolBumpReview {
		writeIPCProtocolReminderLine(stdout, "::notice title=Review IPC protocol version::IPC-facing files changed without protocol declaration changes. Review whether protocolVersion and REQUIRED_CLI_PROTOCOL_VERSION must be bumped.")
	}

	if config.StepSummaryPath != "" {
		err = AppendIPCProtocolReminderSummary(config.StepSummaryPath, result)
		if err != nil {
			writeIPCProtocolReminderLine(stderr, fmt.Sprintf("failed to append GitHub step summary: %v", err))
			return 1
		}
	}

	return 0
}

func writeIPCProtocolReminderLine(writer io.Writer, value string) {
	_, _ = fmt.Fprintln(writer, value)
}

func AnalyzeIPCProtocolReminder(changedFiles []string) IPCProtocolReminderResult {
	changedIPCFiles := []string{}
	changedProtocolFiles := []string{}
	for _, file := range changedFiles {
		normalizedFile := strings.TrimPrefix(strings.TrimSpace(file), "./")
		if normalizedFile == "" {
			continue
		}
		if isProtocolDeclarationPath(normalizedFile) {
			changedProtocolFiles = append(changedProtocolFiles, normalizedFile)
		}
		if matchesAnyIPCProtocolReminderPattern(normalizedFile) {
			changedIPCFiles = append(changedIPCFiles, normalizedFile)
		}
	}

	sort.Strings(changedIPCFiles)
	sort.Strings(changedProtocolFiles)
	hasIPCContractFileChange := len(changedIPCFiles) > 0
	hasProtocolFileChange := len(changedProtocolFiles) > 0
	return IPCProtocolReminderResult{
		ChangedIPCFiles:          changedIPCFiles,
		ChangedProtocolFiles:     changedProtocolFiles,
		NeedsProtocolBumpReview:  hasIPCContractFileChange && !hasProtocolFileChange,
		HasProtocolFileChange:    hasProtocolFileChange,
		HasIPCContractFileChange: hasIPCContractFileChange,
	}
}

func FormatIPCProtocolReminder(result IPCProtocolReminderResult) string {
	if !result.HasIPCContractFileChange {
		return "No IPC contract surfaces changed."
	}
	if result.NeedsProtocolBumpReview {
		return "IPC contract surfaces changed without protocol declaration changes; review whether protocolVersion must be bumped."
	}
	return "IPC contract surfaces changed and protocol declarations changed; verify the bump is intentional and both sides stay aligned."
}

func AppendIPCProtocolReminderSummary(summaryPath string, result IPCProtocolReminderResult) error {
	content := strings.Builder{}
	content.WriteString("## IPC protocol version reminder\n\n")
	content.WriteString(FormatIPCProtocolReminder(result))
	content.WriteString("\n\n")
	if len(result.ChangedIPCFiles) > 0 {
		content.WriteString("Changed IPC-facing files:\n")
		for _, file := range result.ChangedIPCFiles {
			content.WriteString("- `")
			content.WriteString(file)
			content.WriteString("`\n")
		}
		content.WriteString("\n")
	}
	if result.NeedsProtocolBumpReview {
		content.WriteString("If this PR breaks interoperability with the previous protocol generation, bump both `cli/common/clicontract/contract.json` `protocolVersion` and `CliConstants.REQUIRED_CLI_PROTOCOL_VERSION` in the same PR.\n\n")
	}

	file, err := os.OpenFile(summaryPath, os.O_CREATE|os.O_APPEND|os.O_WRONLY, 0o644)
	if err != nil {
		return err
	}
	defer func() {
		_ = file.Close()
	}()
	_, err = file.WriteString(content.String())
	return err
}

func matchesAnyIPCProtocolReminderPattern(file string) bool {
	for _, pattern := range ipcProtocolReminderPatterns {
		if matchesIPCProtocolReminderPattern(file, pattern) {
			return true
		}
	}
	return false
}

func matchesIPCProtocolReminderPattern(file string, pattern string) bool {
	if strings.HasSuffix(pattern, "/**") {
		prefix := strings.TrimSuffix(pattern, "**")
		return strings.HasPrefix(file, prefix)
	}
	return file == pattern
}

func isProtocolDeclarationPath(file string) bool {
	for _, path := range ipcProtocolDeclarationPaths {
		if file == path {
			return true
		}
	}
	return false
}

func gitRepoRoot(ctx context.Context) (string, error) {
	output, err := exec.CommandContext(ctx, "git", "rev-parse", "--show-toplevel").Output()
	if err != nil {
		return "", err
	}
	return strings.TrimSpace(string(output)), nil
}

func gitChangedFiles(ctx context.Context, repoRoot string, baseRef string, headRef string) ([]string, error) {
	rangeSpec := baseRef + "..." + headRef
	command := exec.CommandContext(ctx, "git", "-C", repoRoot, "diff", "--name-only", rangeSpec)
	output, err := command.Output()
	if err != nil {
		return nil, err
	}
	lines := strings.Split(strings.TrimSpace(string(output)), "\n")
	files := []string{}
	for _, line := range lines {
		file := strings.TrimSpace(line)
		if file != "" {
			files = append(files, file)
		}
	}
	return files, nil
}
