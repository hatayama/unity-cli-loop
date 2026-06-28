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
	protocolMinimumVersionFile          = "Packages/src/Editor/Domain/CliConstants.cs"
	protocolMinimumVersionMarker        = "<!-- uloop-protocol-minimum-version-warning -->"
	projectRunnerReleaseTagPrefix       = "uloop-project-runner-v"
	legacyProjectRunnerReleaseTagPrefix = "cli-v"
)

var (
	requiredProtocolVersionPattern           = regexp.MustCompile(`REQUIRED_CLI_PROTOCOL_VERSION\s*=\s*(\d+)`)
	minimumProjectRunnerVersionPattern       = regexp.MustCompile(`MINIMUM_REQUIRED_PROJECT_RUNNER_VERSION\s*=\s*"([^"]+)"`)
	legacyMinimumProjectRunnerVersionPattern = regexp.MustCompile(`MINIMUM_REQUIRED_CLI_VERSION\s*=\s*"([^"]+)"`)
	requiredMinimumProjectRunnerAssets       = []string{
		"uloop-project-runner-darwin-amd64.tar.gz",
		"uloop-project-runner-darwin-amd64.tar.gz.sha256",
		"uloop-project-runner-darwin-arm64.tar.gz",
		"uloop-project-runner-darwin-arm64.tar.gz.sha256",
		"uloop-project-runner-windows-amd64.zip",
		"uloop-project-runner-windows-amd64.zip.sha256",
	}
	requiredLegacyMinimumProjectRunnerAssets = []string{
		"uloop-cli-darwin-amd64.tar.gz",
		"uloop-cli-darwin-amd64.tar.gz.sha256",
		"uloop-cli-darwin-arm64.tar.gz",
		"uloop-cli-darwin-arm64.tar.gz.sha256",
		"uloop-cli-windows-amd64.zip",
		"uloop-cli-windows-amd64.zip.sha256",
	}
)

type minimumProjectRunnerRelease struct {
	Tag            string
	RequiredAssets []string
}

type ProtocolMinimumVersionGuardConfig struct {
	BaseRef string
	HeadRef string
}

type ProtocolMinimumVersionValues struct {
	RequiredProtocolVersion     int
	HasRequiredProtocol         bool
	MinimumProjectRunnerVersion string
}

type ProtocolMinimumVersionGuardResult struct {
	Base                               ProtocolMinimumVersionValues
	Head                               ProtocolMinimumVersionValues
	RequiredProtocolChanged            bool
	MinimumProjectRunnerVersionChanged bool
	NeedsMinimumVersionUpdate          bool
	MinimumCliReleaseProtocolError     string
}

type minimumCliReleaseContract struct {
	ProtocolVersion      *json.RawMessage `json:"protocolVersion"`
	ProjectRunnerVersion string           `json:"projectRunnerVersion"`
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

	releaseTag, err := verifyMinimumCliReleaseProtocolAtRef(ctx, repoRoot, values)
	if err != nil {
		writeProtocolMinimumVersionLine(stderr, err)
		return 1
	}

	writeProtocolMinimumVersionLine(
		stdout,
		fmt.Sprintf(
			"Minimum project runner release %s advertises protocol %d.",
			releaseTag,
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
		_, err = verifyMinimumCliReleaseProtocolAtRef(ctx, repoRoot, result.Head)
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
	minimumProjectRunnerVersionChanged := base.MinimumProjectRunnerVersion != head.MinimumProjectRunnerVersion

	return ProtocolMinimumVersionGuardResult{
		Base:                               base,
		Head:                               head,
		RequiredProtocolChanged:            requiredProtocolChanged,
		MinimumProjectRunnerVersionChanged: minimumProjectRunnerVersionChanged,
		NeedsMinimumVersionUpdate:          requiredProtocolChanged && !minimumProjectRunnerVersionChanged,
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

	minimumProjectRunnerVersion, ok := parseMinimumProjectRunnerVersion(text)
	if !ok {
		return ProtocolMinimumVersionValues{}, fmt.Errorf("%s does not define MINIMUM_REQUIRED_PROJECT_RUNNER_VERSION", protocolMinimumVersionFile)
	}
	values.MinimumProjectRunnerVersion = minimumProjectRunnerVersion
	return values, nil
}

func VerifyMinimumCliReleaseProtocol(values ProtocolMinimumVersionValues, contractContent []byte) error {
	return verifyMinimumProjectRunnerReleaseProtocol(
		projectRunnerReleaseTagPrefix+values.MinimumProjectRunnerVersion,
		values,
		contractContent)
}

func parseMinimumProjectRunnerVersion(text string) (string, bool) {
	minimumMatches := minimumProjectRunnerVersionPattern.FindStringSubmatch(text)
	if len(minimumMatches) == 2 {
		return minimumMatches[1], true
	}

	legacyMinimumMatches := legacyMinimumProjectRunnerVersionPattern.FindStringSubmatch(text)
	if len(legacyMinimumMatches) == 2 {
		return legacyMinimumMatches[1], true
	}
	return "", false
}

func verifyMinimumProjectRunnerReleaseProtocol(
	releaseTag string,
	values ProtocolMinimumVersionValues,
	contractContent []byte,
) error {
	if !values.HasRequiredProtocol {
		return fmt.Errorf("%s does not define REQUIRED_CLI_PROTOCOL_VERSION", protocolMinimumVersionFile)
	}

	contract := minimumCliReleaseContract{}
	err := json.Unmarshal(contractContent, &contract)
	if err != nil {
		return fmt.Errorf("project runner release contract is invalid JSON: %w", err)
	}
	protocolVersion, hasProtocolVersion := minimumCliReleaseProtocolVersion(contract.ProtocolVersion)
	if !hasProtocolVersion {
		return fmt.Errorf(
			"project runner release %s does not define protocolVersion",
			releaseTag)
	}
	if protocolVersion != values.RequiredProtocolVersion {
		return fmt.Errorf(
			"unity package requires protocol %d, but project runner release %s advertises protocol %d",
			values.RequiredProtocolVersion,
			releaseTag,
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
) (string, error) {
	releases := minimumProjectRunnerReleases(values.MinimumProjectRunnerVersion)
	unavailableReleases := []string{}
	for _, release := range releases {
		contractContent, err := protocolMinimumVersionFileAtRef(ctx, repoRoot, release.Tag, "cli/contract.json")
		if err != nil {
			unavailableReleases = append(unavailableReleases, release.Tag)
			continue
		}
		if err := verifyMinimumProjectRunnerReleaseProtocol(release.Tag, values, []byte(contractContent)); err != nil {
			return "", err
		}
		return verifyMinimumCliReleaseIsPublished(ctx, repoRoot, release)
	}

	return "", fmt.Errorf(
		"project runner release %s does not provide cli/contract.json",
		strings.Join(unavailableReleases, " or "))
}

func minimumProjectRunnerReleases(version string) []minimumProjectRunnerRelease {
	return []minimumProjectRunnerRelease{
		{
			Tag:            projectRunnerReleaseTagPrefix + version,
			RequiredAssets: requiredMinimumProjectRunnerAssets,
		},
		{
			Tag:            legacyProjectRunnerReleaseTagPrefix + version,
			RequiredAssets: requiredLegacyMinimumProjectRunnerAssets,
		},
	}
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

func verifyMinimumCliReleaseIsPublished(ctx context.Context, repoRoot string, release minimumProjectRunnerRelease) (string, error) {
	output, err := runProtocolMinimumVersionOutput(
		ctx,
		repoRoot,
		"gh",
		"release",
		"view",
		release.Tag,
		"--json",
		"isDraft,assets")
	if err != nil {
		return "", fmt.Errorf("project runner release %s is not published with complete native assets: %w", release.Tag, err)
	}

	releaseView := minimumCliReleaseView{}
	if err := json.Unmarshal([]byte(output), &releaseView); err != nil {
		return "", fmt.Errorf("project runner release %s metadata is invalid JSON: %w", release.Tag, err)
	}
	if releaseView.IsDraft {
		return "", fmt.Errorf("project runner release %s is still draft", release.Tag)
	}
	if missingAsset := missingMinimumCliReleaseAsset(releaseView.Assets, release.RequiredAssets); missingAsset != "" {
		return "", fmt.Errorf("project runner release %s is missing release asset %s", release.Tag, missingAsset)
	}
	return release.Tag, nil
}

func missingMinimumCliReleaseAsset(assets []minimumCliReleaseAsset, requiredAssetNames []string) string {
	availableAssets := map[string]bool{}
	for _, asset := range assets {
		if asset.Size > 0 {
			availableAssets[asset.Name] = true
		}
	}

	for _, assetName := range requiredAssetNames {
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
		builder.WriteString("Protocol version changed, but `MINIMUM_REQUIRED_PROJECT_RUNNER_VERSION` did not.\n\n")
	} else if result.RequiredProtocolChanged {
		builder.WriteString("Protocol version changed, but `MINIMUM_REQUIRED_PROJECT_RUNNER_VERSION` does not point to a published project runner release that advertises the required protocol.\n\n")
	} else {
		builder.WriteString("`MINIMUM_REQUIRED_PROJECT_RUNNER_VERSION` changed, but it does not point to a published project runner release that advertises the required protocol.\n\n")
	}
	builder.WriteString("- Base required protocol: ")
	builder.WriteString(protocolMinimumVersionValueLabel(result.Base))
	builder.WriteString("\n")
	builder.WriteString("- Head required protocol: ")
	builder.WriteString(protocolMinimumVersionValueLabel(result.Head))
	builder.WriteString("\n")
	builder.WriteString("- Current minimum project runner: `")
	builder.WriteString(result.Head.MinimumProjectRunnerVersion)
	builder.WriteString("`\n")
	if result.MinimumCliReleaseProtocolError != "" {
		builder.WriteString("- Release check: ")
		builder.WriteString(protocolMinimumVersionErrorLabel(result.MinimumCliReleaseProtocolError))
		builder.WriteString("\n")
	}
	builder.WriteString("\n")
	builder.WriteString("Update `MINIMUM_REQUIRED_PROJECT_RUNNER_VERSION` to a published project runner release that advertises the new protocol before releasing the Unity package.")
	return builder.String()
}

func protocolMinimumVersionGuardNeedsAction(result ProtocolMinimumVersionGuardResult) bool {
	return result.NeedsMinimumVersionUpdate || result.MinimumCliReleaseProtocolError != ""
}

func protocolMinimumVersionGuardNeedsReleaseCheck(result ProtocolMinimumVersionGuardResult) bool {
	if result.NeedsMinimumVersionUpdate {
		return false
	}
	return result.RequiredProtocolChanged || result.MinimumProjectRunnerVersionChanged
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
