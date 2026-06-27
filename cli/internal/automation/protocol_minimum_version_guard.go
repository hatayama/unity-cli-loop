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
	"regexp"
	"strconv"
	"strings"
)

const (
	protocolMinimumVersionFile   = "Packages/src/Editor/Domain/CliConstants.cs"
	protocolMinimumVersionMarker = "<!-- uloop-protocol-minimum-version-warning -->"
	cliReleaseTagPrefix          = "cli-v"
)

var (
	requiredProtocolVersionPattern  = regexp.MustCompile(`REQUIRED_CLI_PROTOCOL_VERSION\s*=\s*(\d+)`)
	minimumCliVersionPattern        = regexp.MustCompile(`MINIMUM_REQUIRED_CLI_VERSION\s*=\s*"([^"]+)"`)
	requiredMinimumCliReleaseAssets = []string{
		"uloop-cli-darwin-amd64.tar.gz",
		"uloop-cli-darwin-amd64.tar.gz.sha256",
		"uloop-cli-darwin-arm64.tar.gz",
		"uloop-cli-darwin-arm64.tar.gz.sha256",
		"uloop-cli-windows-amd64.zip",
		"uloop-cli-windows-amd64.zip.sha256",
		"uloop-darwin-amd64.tar.gz",
		"uloop-darwin-amd64.tar.gz.sha256",
		"uloop-darwin-arm64.tar.gz",
		"uloop-darwin-arm64.tar.gz.sha256",
		"uloop-windows-amd64.zip",
		"uloop-windows-amd64.zip.sha256",
	}
)

type ProtocolMinimumVersionGuardConfig struct {
	BaseRef string
	HeadRef string
}

type ProtocolMinimumVersionValues struct {
	RequiredProtocolVersion int
	HasRequiredProtocol     bool
	MinimumCliVersion       string
}

type ProtocolMinimumVersionGuardResult struct {
	Base                           ProtocolMinimumVersionValues
	Head                           ProtocolMinimumVersionValues
	RequiredProtocolChanged        bool
	MinimumCliVersionChanged       bool
	NeedsMinimumVersionUpdate      bool
	MinimumCliReleaseProtocolError string
}

type minimumCliReleaseContract struct {
	ProtocolVersion *json.RawMessage `json:"protocolVersion"`
	CliVersion      string           `json:"cliVersion"`
}

type minimumCliReleaseView struct {
	IsDraft bool                     `json:"isDraft"`
	Assets  []minimumCliReleaseAsset `json:"assets"`
}

type minimumCliReleaseAsset struct {
	Name string `json:"name"`
	Size int64  `json:"size"`
}

func RunProtocolMinimumVersionGuard(
	ctx context.Context,
	stdout io.Writer,
	stderr io.Writer,
	config ProtocolMinimumVersionGuardConfig,
) int {
	result, err := AnalyzeProtocolMinimumVersionGuardForRefs(ctx, config)
	if err != nil {
		writeProtocolMinimumVersionLine(stderr, err)
		return 1
	}
	if protocolMinimumVersionGuardNeedsAction(result) {
		writeProtocolMinimumVersionLine(stderr, FormatProtocolMinimumVersionWarning(result))
		return 1
	}

	writeProtocolMinimumVersionLine(stdout, "Protocol minimum version guard passed.")
	return 0
}

func RunMinimumCliReleaseProtocolCheck(ctx context.Context, stdout io.Writer, stderr io.Writer, ref string) int {
	repoRoot, err := gitRepoRoot(ctx)
	if err != nil {
		writeProtocolMinimumVersionLine(stderr, fmt.Sprintf("failed to resolve git repository root: %v", err))
		return 1
	}

	content, err := minimumCliReleaseProtocolFile(ctx, repoRoot, ref)
	if err != nil {
		writeProtocolMinimumVersionLine(stderr, fmt.Sprintf("failed to read %s: %v", protocolMinimumVersionFile, err))
		return 1
	}
	values, err := ParseProtocolMinimumVersionValues(content)
	if err != nil {
		writeProtocolMinimumVersionLine(stderr, err)
		return 1
	}
	if !values.HasRequiredProtocol {
		writeProtocolMinimumVersionLine(stderr, protocolMinimumVersionFile+" does not define REQUIRED_CLI_PROTOCOL_VERSION")
		return 1
	}

	if err := verifyMinimumCliReleaseProtocolAtRef(ctx, repoRoot, values); err != nil {
		writeProtocolMinimumVersionLine(stderr, err)
		return 1
	}

	writeProtocolMinimumVersionLine(
		stdout,
		fmt.Sprintf(
			"Minimum CLI release %s%s advertises protocol %d.",
			cliReleaseTagPrefix,
			values.MinimumCliVersion,
			values.RequiredProtocolVersion))
	return 0
}

func AnalyzeProtocolMinimumVersionGuardForRefs(
	ctx context.Context,
	config ProtocolMinimumVersionGuardConfig,
) (ProtocolMinimumVersionGuardResult, error) {
	if config.BaseRef == "" {
		return ProtocolMinimumVersionGuardResult{}, fmt.Errorf("--base is required")
	}
	if config.HeadRef == "" {
		config.HeadRef = "HEAD"
	}

	repoRoot, err := gitRepoRoot(ctx)
	if err != nil {
		return ProtocolMinimumVersionGuardResult{}, fmt.Errorf("failed to resolve git repository root: %w", err)
	}

	baseValues, err := protocolMinimumVersionValuesAtRef(ctx, repoRoot, config.BaseRef)
	if err != nil {
		return ProtocolMinimumVersionGuardResult{}, err
	}
	headValues, err := protocolMinimumVersionValuesAtRef(ctx, repoRoot, config.HeadRef)
	if err != nil {
		return ProtocolMinimumVersionGuardResult{}, err
	}

	result := AnalyzeProtocolMinimumVersionGuard(baseValues, headValues)
	if protocolMinimumVersionGuardNeedsReleaseCheck(result) {
		err = verifyMinimumCliReleaseProtocolAtRef(ctx, repoRoot, result.Head)
		if err != nil {
			result.MinimumCliReleaseProtocolError = err.Error()
		}
	}
	return result, nil
}

func AnalyzeProtocolMinimumVersionGuard(
	base ProtocolMinimumVersionValues,
	head ProtocolMinimumVersionValues,
) ProtocolMinimumVersionGuardResult {
	requiredProtocolChanged := base.HasRequiredProtocol != head.HasRequiredProtocol ||
		base.RequiredProtocolVersion != head.RequiredProtocolVersion
	minimumCliVersionChanged := base.MinimumCliVersion != head.MinimumCliVersion

	return ProtocolMinimumVersionGuardResult{
		Base:                      base,
		Head:                      head,
		RequiredProtocolChanged:   requiredProtocolChanged,
		MinimumCliVersionChanged:  minimumCliVersionChanged,
		NeedsMinimumVersionUpdate: requiredProtocolChanged && !minimumCliVersionChanged,
	}
}

func ParseProtocolMinimumVersionValues(content []byte) (ProtocolMinimumVersionValues, error) {
	text := string(content)
	values := ProtocolMinimumVersionValues{}

	requiredMatches := requiredProtocolVersionPattern.FindStringSubmatch(text)
	if len(requiredMatches) == 2 {
		requiredProtocolVersion, err := strconv.Atoi(requiredMatches[1])
		if err != nil {
			return ProtocolMinimumVersionValues{}, fmt.Errorf("REQUIRED_CLI_PROTOCOL_VERSION is not an integer: %w", err)
		}
		values.RequiredProtocolVersion = requiredProtocolVersion
		values.HasRequiredProtocol = true
	}

	minimumMatches := minimumCliVersionPattern.FindStringSubmatch(text)
	if len(minimumMatches) != 2 {
		return ProtocolMinimumVersionValues{}, fmt.Errorf("%s does not define MINIMUM_REQUIRED_CLI_VERSION", protocolMinimumVersionFile)
	}
	values.MinimumCliVersion = minimumMatches[1]
	return values, nil
}

func VerifyMinimumCliReleaseProtocol(values ProtocolMinimumVersionValues, contractContent []byte) error {
	if !values.HasRequiredProtocol {
		return fmt.Errorf("%s does not define REQUIRED_CLI_PROTOCOL_VERSION", protocolMinimumVersionFile)
	}

	contract := minimumCliReleaseContract{}
	err := json.Unmarshal(contractContent, &contract)
	if err != nil {
		return fmt.Errorf("CLI release contract is invalid JSON: %w", err)
	}
	protocolVersion, hasProtocolVersion := minimumCliReleaseProtocolVersion(contract.ProtocolVersion)
	if !hasProtocolVersion {
		return fmt.Errorf(
			"CLI release %s%s does not define protocolVersion",
			cliReleaseTagPrefix,
			values.MinimumCliVersion)
	}
	if protocolVersion != values.RequiredProtocolVersion {
		return fmt.Errorf(
			"unity package requires protocol %d, but CLI release %s%s advertises protocol %d",
			values.RequiredProtocolVersion,
			cliReleaseTagPrefix,
			values.MinimumCliVersion,
			protocolVersion)
	}
	return nil
}

func minimumCliReleaseProtocolVersion(value *json.RawMessage) (int, bool) {
	if value == nil {
		return 0, false
	}

	protocolVersion, err := strconv.Atoi(strings.TrimSpace(string(*value)))
	if err != nil {
		return 0, false
	}
	return protocolVersion, true
}

func verifyMinimumCliReleaseProtocolAtRef(
	ctx context.Context,
	repoRoot string,
	values ProtocolMinimumVersionValues,
) error {
	releaseTag := cliReleaseTagPrefix + values.MinimumCliVersion
	contractContent, err := protocolMinimumVersionFileAtRef(ctx, repoRoot, releaseTag, "cli/contract.json")
	if err != nil {
		return fmt.Errorf("CLI release %s does not provide cli/contract.json", releaseTag)
	}
	if err := VerifyMinimumCliReleaseProtocol(values, []byte(contractContent)); err != nil {
		return err
	}
	return verifyMinimumCliReleaseIsPublished(ctx, repoRoot, releaseTag)
}

func minimumCliReleaseProtocolFile(ctx context.Context, repoRoot string, ref string) ([]byte, error) {
	if ref == "" {
		return os.ReadFile(filepath.Join(repoRoot, protocolMinimumVersionFile))
	}

	content, err := protocolMinimumVersionFileAtRef(ctx, repoRoot, ref, protocolMinimumVersionFile)
	if err != nil {
		return nil, err
	}
	return []byte(content), nil
}

func verifyMinimumCliReleaseIsPublished(ctx context.Context, repoRoot string, releaseTag string) error {
	output, err := runProtocolMinimumVersionOutput(
		ctx,
		repoRoot,
		"gh",
		"release",
		"view",
		releaseTag,
		"--json",
		"isDraft,assets")
	if err != nil {
		return fmt.Errorf("CLI release %s is not published with complete native assets: %w", releaseTag, err)
	}

	releaseView := minimumCliReleaseView{}
	if err := json.Unmarshal([]byte(output), &releaseView); err != nil {
		return fmt.Errorf("CLI release %s metadata is invalid JSON: %w", releaseTag, err)
	}
	if releaseView.IsDraft {
		return fmt.Errorf("CLI release %s is still draft", releaseTag)
	}
	if missingAsset := missingMinimumCliReleaseAsset(releaseView.Assets); missingAsset != "" {
		return fmt.Errorf("CLI release %s is missing release asset %s", releaseTag, missingAsset)
	}
	return nil
}

func missingMinimumCliReleaseAsset(assets []minimumCliReleaseAsset) string {
	availableAssets := map[string]bool{}
	for _, asset := range assets {
		if asset.Size > 0 {
			availableAssets[asset.Name] = true
		}
	}

	for _, assetName := range requiredMinimumCliReleaseAssets {
		if !availableAssets[assetName] {
			return assetName
		}
	}
	return ""
}

func FormatProtocolMinimumVersionWarning(result ProtocolMinimumVersionGuardResult) string {
	builder := strings.Builder{}
	builder.WriteString(protocolMinimumVersionMarker)
	builder.WriteString("\n")
	if result.NeedsMinimumVersionUpdate {
		builder.WriteString("Protocol version changed, but `MINIMUM_REQUIRED_CLI_VERSION` did not.\n\n")
	} else if result.RequiredProtocolChanged {
		builder.WriteString("Protocol version changed, but `MINIMUM_REQUIRED_CLI_VERSION` does not point to a published CLI release that advertises the required protocol.\n\n")
	} else {
		builder.WriteString("`MINIMUM_REQUIRED_CLI_VERSION` changed, but it does not point to a published CLI release that advertises the required protocol.\n\n")
	}
	builder.WriteString("- Base required protocol: ")
	builder.WriteString(protocolMinimumVersionValueLabel(result.Base))
	builder.WriteString("\n")
	builder.WriteString("- Head required protocol: ")
	builder.WriteString(protocolMinimumVersionValueLabel(result.Head))
	builder.WriteString("\n")
	builder.WriteString("- Current minimum CLI: `")
	builder.WriteString(result.Head.MinimumCliVersion)
	builder.WriteString("`\n")
	if result.MinimumCliReleaseProtocolError != "" {
		builder.WriteString("- Release check: ")
		builder.WriteString(protocolMinimumVersionErrorLabel(result.MinimumCliReleaseProtocolError))
		builder.WriteString("\n")
	}
	builder.WriteString("\n")
	builder.WriteString("Update `MINIMUM_REQUIRED_CLI_VERSION` to a published CLI release that advertises the new protocol before releasing the Unity package.")
	return builder.String()
}

func protocolMinimumVersionGuardNeedsAction(result ProtocolMinimumVersionGuardResult) bool {
	return result.NeedsMinimumVersionUpdate || result.MinimumCliReleaseProtocolError != ""
}

func protocolMinimumVersionGuardNeedsReleaseCheck(result ProtocolMinimumVersionGuardResult) bool {
	if result.NeedsMinimumVersionUpdate {
		return false
	}
	return result.RequiredProtocolChanged || result.MinimumCliVersionChanged
}

func protocolMinimumVersionValueLabel(values ProtocolMinimumVersionValues) string {
	if !values.HasRequiredProtocol {
		return "`<missing>`"
	}
	return "`" + strconv.Itoa(values.RequiredProtocolVersion) + "`"
}

func protocolMinimumVersionErrorLabel(value string) string {
	trimmedValue := strings.TrimSpace(value)
	singleLineValue := strings.ReplaceAll(trimmedValue, "\n", " ")
	return "`" + singleLineValue + "`"
}

func protocolMinimumVersionValuesAtRef(
	ctx context.Context,
	repoRoot string,
	ref string,
) (ProtocolMinimumVersionValues, error) {
	content, err := protocolMinimumVersionFileAtRef(ctx, repoRoot, ref, protocolMinimumVersionFile)
	if err != nil {
		return ProtocolMinimumVersionValues{}, err
	}
	return ParseProtocolMinimumVersionValues([]byte(content))
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

func writeProtocolMinimumVersionLine(writer io.Writer, values ...any) {
	// CI status output failures cannot be recovered after the command outcome is known.
	_, _ = fmt.Fprintln(writer, values...)
}
