package automation

import (
	"context"
	"encoding/json"
	"fmt"
	"io"
	"os"
	"path/filepath"
	"regexp"
	"strconv"
	"strings"
)

const (
	cliContractFile                     = "common/clicontract/contract.json"
	legacyRunnerContractFile            = "cli/contract.json"
	dispatcherContractFile              = "dispatcher/dispatcher-contract.json"
	legacyDispatcherContractFile        = "cli/dispatcher-contract.json"
	dispatcherReleaseTagPrefix          = "dispatcher-v"
	unityPackageCliPinFile              = "Packages/src/project-runner-pin.json"
	unityProjectCliPinFile              = ".uloop/project-runner-pin.json"
	minimumDispatcherContractVersion    = 1
	minimumDispatcherVersionDescription = "minimumDispatcherVersion"
)

var minimumDispatcherVersionPattern = regexp.MustCompile(`MINIMUM_REQUIRED_DISPATCHER_VERSION\s*=\s*"([^"]+)"`)

type dispatcherMinimumVersionValues struct {
	CurrentProjectRunnerVersion        string
	CurrentDispatcherVersion           string
	CurrentDispatcherContractVersion   int
	MinimumDispatcherVersion           string
	PackagePinProjectRunnerVersion     string
	PackagePinMinimumDispatcherVersion string
	ProjectPinProjectRunnerVersion     string
	ProjectPinMinimumDispatcherVersion string
}

type dispatcherMinimumVersionCliContract struct {
	ProjectRunnerVersion string `json:"projectRunnerVersion"`
}

type dispatcherMinimumVersionContract struct {
	DispatcherVersion         string `json:"dispatcherVersion"`
	DispatcherContractVersion int    `json:"dispatcherContractVersion"`
}

type dispatcherMinimumVersionReleaseContract struct {
	DispatcherVersion         string           `json:"dispatcherVersion"`
	DispatcherContractVersion *json.RawMessage `json:"dispatcherContractVersion"`
}

type dispatcherMinimumVersionCliPin struct {
	SchemaVersion            int    `json:"schemaVersion"`
	PackageName              string `json:"packageName"`
	PackageVersion           string `json:"packageVersion"`
	ProjectRunnerVersion     string `json:"projectRunnerVersion"`
	RequiredProtocolVersion  int    `json:"requiredProtocolVersion"`
	MinimumDispatcherVersion string `json:"minimumDispatcherVersion"`
}

func RunDispatcherMinimumVersionCheck(ctx context.Context, stdout io.Writer, stderr io.Writer, ref string) int {
	repoRoot, err := gitRepoRoot(ctx)
	if err != nil {
		writeDispatcherMinimumVersionLine(stderr, fmt.Sprintf("failed to resolve git repository root: %v", err))
		return 1
	}

	values, err := dispatcherMinimumVersionValuesAtRef(ctx, repoRoot, ref)
	if err != nil {
		writeDispatcherMinimumVersionLine(stderr, err)
		return 1
	}
	if err := verifyDispatcherMinimumVersionAtRef(ctx, repoRoot, values); err != nil {
		writeDispatcherMinimumVersionLine(stderr, err)
		return 1
	}

	writeDispatcherMinimumVersionLine(stdout, "Dispatcher minimum version guard passed.")
	return 0
}

func dispatcherMinimumVersionValuesAtRef(
	ctx context.Context,
	repoRoot string,
	ref string,
) (dispatcherMinimumVersionValues, error) {
	cliContractContent, err := dispatcherMinimumVersionFileAtRef(ctx, repoRoot, ref, cliContractFile)
	if err != nil {
		return dispatcherMinimumVersionValues{}, err
	}
	dispatcherContractContent, err := dispatcherMinimumVersionFileAtRef(ctx, repoRoot, ref, dispatcherContractFile)
	if err != nil {
		return dispatcherMinimumVersionValues{}, err
	}
	constantsContent, err := dispatcherMinimumVersionFileAtRef(ctx, repoRoot, ref, protocolMinimumVersionFile)
	if err != nil {
		return dispatcherMinimumVersionValues{}, err
	}
	packagePinContent, err := dispatcherMinimumVersionFileAtRef(ctx, repoRoot, ref, unityPackageCliPinFile)
	if err != nil {
		return dispatcherMinimumVersionValues{}, err
	}
	projectPinContent, err := dispatcherMinimumVersionFileAtRef(ctx, repoRoot, ref, unityProjectCliPinFile)
	if err != nil {
		return dispatcherMinimumVersionValues{}, err
	}

	return parseDispatcherMinimumVersionValues(
		[]byte(cliContractContent),
		[]byte(dispatcherContractContent),
		[]byte(constantsContent),
		[]byte(packagePinContent),
		[]byte(projectPinContent))
}

func dispatcherMinimumVersionFileAtRef(
	ctx context.Context,
	repoRoot string,
	ref string,
	file string,
) (string, error) {
	if ref != "" {
		return protocolMinimumVersionFileAtRef(ctx, repoRoot, ref, file)
	}

	content, err := os.ReadFile(filepath.Join(repoRoot, file))
	if err != nil {
		return "", fmt.Errorf("failed to read %s: %w", file, err)
	}
	return string(content), nil
}

func parseDispatcherMinimumVersionValues(
	cliContractContent []byte,
	dispatcherContractContent []byte,
	constantsContent []byte,
	packagePinContent []byte,
	projectPinContent []byte,
) (dispatcherMinimumVersionValues, error) {
	cliContract, err := parseDispatcherMinimumVersionCliContract(cliContractContent)
	if err != nil {
		return dispatcherMinimumVersionValues{}, err
	}
	dispatcherContract, err := parseDispatcherMinimumVersionContract(dispatcherContractContent)
	if err != nil {
		return dispatcherMinimumVersionValues{}, err
	}
	minimumDispatcherVersion, err := parseMinimumDispatcherVersion(constantsContent)
	if err != nil {
		return dispatcherMinimumVersionValues{}, err
	}
	packagePin, err := parseDispatcherMinimumVersionPin(unityPackageCliPinFile, packagePinContent)
	if err != nil {
		return dispatcherMinimumVersionValues{}, err
	}
	projectPin, err := parseDispatcherMinimumVersionPin(unityProjectCliPinFile, projectPinContent)
	if err != nil {
		return dispatcherMinimumVersionValues{}, err
	}

	values := dispatcherMinimumVersionValues{
		CurrentProjectRunnerVersion:        cliContract.ProjectRunnerVersion,
		CurrentDispatcherVersion:           dispatcherContract.DispatcherVersion,
		CurrentDispatcherContractVersion:   dispatcherContract.DispatcherContractVersion,
		MinimumDispatcherVersion:           minimumDispatcherVersion,
		PackagePinProjectRunnerVersion:     packagePin.ProjectRunnerVersion,
		PackagePinMinimumDispatcherVersion: packagePin.MinimumDispatcherVersion,
		ProjectPinProjectRunnerVersion:     projectPin.ProjectRunnerVersion,
		ProjectPinMinimumDispatcherVersion: projectPin.MinimumDispatcherVersion,
	}
	return values, validateDispatcherMinimumVersionValues(values)
}

func parseDispatcherMinimumVersionCliContract(content []byte) (dispatcherMinimumVersionCliContract, error) {
	contract := dispatcherMinimumVersionCliContract{}
	if err := json.Unmarshal(content, &contract); err != nil {
		return dispatcherMinimumVersionCliContract{}, fmt.Errorf("%s is invalid JSON: %w", cliContractFile, err)
	}
	if contract.ProjectRunnerVersion == "" {
		return dispatcherMinimumVersionCliContract{}, fmt.Errorf("%s does not define projectRunnerVersion", cliContractFile)
	}
	return contract, nil
}

func parseDispatcherMinimumVersionContract(content []byte) (dispatcherMinimumVersionContract, error) {
	contract := dispatcherMinimumVersionContract{}
	if err := json.Unmarshal(content, &contract); err != nil {
		return dispatcherMinimumVersionContract{}, fmt.Errorf("%s is invalid JSON: %w", dispatcherContractFile, err)
	}
	if contract.DispatcherVersion == "" {
		return dispatcherMinimumVersionContract{}, fmt.Errorf("%s does not define dispatcherVersion", dispatcherContractFile)
	}
	if contract.DispatcherContractVersion < minimumDispatcherContractVersion {
		return dispatcherMinimumVersionContract{}, dispatcherContractVersionTooLowError(
			dispatcherContractFile,
			contract.DispatcherContractVersion)
	}
	return contract, nil
}

func parseMinimumDispatcherVersion(content []byte) (string, error) {
	matches := minimumDispatcherVersionPattern.FindStringSubmatch(string(content))
	if len(matches) != 2 {
		return "", fmt.Errorf("%s does not define MINIMUM_REQUIRED_DISPATCHER_VERSION", protocolMinimumVersionFile)
	}
	return matches[1], nil
}

func parseDispatcherMinimumVersionPin(path string, content []byte) (dispatcherMinimumVersionCliPin, error) {
	pin := dispatcherMinimumVersionCliPin{}
	if err := json.Unmarshal(content, &pin); err != nil {
		return dispatcherMinimumVersionCliPin{}, fmt.Errorf("%s is invalid JSON: %w", path, err)
	}
	if pin.ProjectRunnerVersion == "" {
		return dispatcherMinimumVersionCliPin{}, fmt.Errorf("%s does not define projectRunnerVersion", path)
	}
	if pin.MinimumDispatcherVersion == "" {
		return dispatcherMinimumVersionCliPin{}, fmt.Errorf("%s does not define %s", path, minimumDispatcherVersionDescription)
	}
	return pin, nil
}

func validateDispatcherMinimumVersionValues(values dispatcherMinimumVersionValues) error {
	if values.PackagePinProjectRunnerVersion != values.CurrentProjectRunnerVersion {
		return fmt.Errorf("%s projectRunnerVersion %q does not match %s projectRunnerVersion %q",
			unityPackageCliPinFile,
			values.PackagePinProjectRunnerVersion,
			cliContractFile,
			values.CurrentProjectRunnerVersion)
	}
	if values.ProjectPinProjectRunnerVersion != values.PackagePinProjectRunnerVersion {
		return fmt.Errorf("%s projectRunnerVersion %q does not match %s projectRunnerVersion %q",
			unityProjectCliPinFile,
			values.ProjectPinProjectRunnerVersion,
			unityPackageCliPinFile,
			values.PackagePinProjectRunnerVersion)
	}
	if values.PackagePinMinimumDispatcherVersion != values.MinimumDispatcherVersion {
		return fmt.Errorf("%s %s %q does not match %s MINIMUM_REQUIRED_DISPATCHER_VERSION %q",
			unityPackageCliPinFile,
			minimumDispatcherVersionDescription,
			values.PackagePinMinimumDispatcherVersion,
			protocolMinimumVersionFile,
			values.MinimumDispatcherVersion)
	}
	if values.ProjectPinMinimumDispatcherVersion != values.PackagePinMinimumDispatcherVersion {
		return fmt.Errorf("%s %s %q does not match %s %s %q",
			unityProjectCliPinFile,
			minimumDispatcherVersionDescription,
			values.ProjectPinMinimumDispatcherVersion,
			unityPackageCliPinFile,
			minimumDispatcherVersionDescription,
			values.PackagePinMinimumDispatcherVersion)
	}
	return nil
}

func verifyDispatcherMinimumVersionAtRef(
	ctx context.Context,
	repoRoot string,
	values dispatcherMinimumVersionValues,
) error {
	if values.MinimumDispatcherVersion == values.CurrentDispatcherVersion {
		return verifyCurrentDispatcherMinimumVersion(values)
	}

	releaseTag := dispatcherReleaseTagPrefix + values.MinimumDispatcherVersion
	contractContent, err := dispatcherContractFileAtRef(ctx, repoRoot, releaseTag)
	if err != nil {
		return fmt.Errorf("dispatcher release %s does not provide %s or %s", releaseTag, dispatcherContractFile, legacyDispatcherContractFile)
	}
	return verifyMinimumCliReleaseDispatcherContract(values, []byte(contractContent))
}

// dispatcherContractFileAtRef reads the dispatcher release contract at a git ref.
// Dispatcher releases published before the cli/ directory split still provide the
// contract at the pre-split path, so this falls back to it when the new path is
// missing at the given ref.
func dispatcherContractFileAtRef(ctx context.Context, repoRoot string, ref string) (string, error) {
	content, err := protocolMinimumVersionFileAtRef(ctx, repoRoot, ref, dispatcherContractFile)
	if err == nil {
		return content, nil
	}
	if !isMissingFileAtRefError(err, dispatcherContractFile) {
		return "", err
	}
	return protocolMinimumVersionFileAtRef(ctx, repoRoot, ref, legacyDispatcherContractFile)
}

func verifyCurrentDispatcherMinimumVersion(values dispatcherMinimumVersionValues) error {
	if values.CurrentDispatcherContractVersion < minimumDispatcherContractVersion {
		return dispatcherContractVersionTooLowError(dispatcherContractFile, values.CurrentDispatcherContractVersion)
	}
	return nil
}

func verifyMinimumCliReleaseDispatcherContract(values dispatcherMinimumVersionValues, contractContent []byte) error {
	contract := dispatcherMinimumVersionReleaseContract{}
	if err := json.Unmarshal(contractContent, &contract); err != nil {
		return fmt.Errorf("dispatcher release contract is invalid JSON: %w", err)
	}
	if contract.DispatcherVersion != "" && contract.DispatcherVersion != values.MinimumDispatcherVersion {
		return fmt.Errorf(
			"dispatcher release %s%s contract declares dispatcherVersion %q",
			dispatcherReleaseTagPrefix,
			values.MinimumDispatcherVersion,
			contract.DispatcherVersion)
	}

	releaseLabel := dispatcherReleaseTagPrefix + values.MinimumDispatcherVersion
	dispatcherContractVersion, hasDispatcherContractVersion, err := dispatcherMinimumReleaseContractVersion(
		releaseLabel,
		contract.DispatcherContractVersion)
	if err != nil {
		return err
	}
	if !hasDispatcherContractVersion {
		return fmt.Errorf("dispatcher release %s does not define dispatcherContractVersion", releaseLabel)
	}
	if dispatcherContractVersion < minimumDispatcherContractVersion {
		return dispatcherContractVersionTooLowError("dispatcher release "+releaseLabel, dispatcherContractVersion)
	}
	if dispatcherContractVersion < values.CurrentDispatcherContractVersion {
		return fmt.Errorf(
			"unity package requires dispatcher contract %d, but dispatcher release %s advertises dispatcher contract %d",
			values.CurrentDispatcherContractVersion,
			releaseLabel,
			dispatcherContractVersion)
	}
	return nil
}

func dispatcherMinimumReleaseContractVersion(releaseLabel string, value *json.RawMessage) (int, bool, error) {
	if value == nil {
		return 0, false, nil
	}

	rawValue := strings.TrimSpace(string(*value))
	dispatcherContractVersion, err := strconv.Atoi(rawValue)
	if err != nil {
		return 0, true, fmt.Errorf(
			"dispatcher release %s dispatcherContractVersion must be an integer, got %s",
			releaseLabel,
			rawValue)
	}
	return dispatcherContractVersion, true, nil
}

func dispatcherContractVersionTooLowError(subject string, value int) error {
	return fmt.Errorf(
		"%s dispatcherContractVersion must be at least %d, got %d",
		subject,
		minimumDispatcherContractVersion,
		value)
}

func writeDispatcherMinimumVersionLine(writer io.Writer, values ...any) {
	_, _ = fmt.Fprintln(writer, values...)
}
