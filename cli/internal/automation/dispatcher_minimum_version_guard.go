package automation

import (
	"bytes"
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"os"
	"path/filepath"
	"strconv"
	"strings"
)

const (
	dispatcherContractFile              = "cli/contract.json"
	unityPackageCliPinFile              = "Packages/src/cli-pin.json"
	unityProjectCliPinFile              = ".uloop/cli-pin.json"
	minimumDispatcherContractVersion    = 1
	minimumDispatcherVersionDescription = "minimumDispatcherVersion"
)

type dispatcherMinimumVersionValues struct {
	CurrentCliVersion                  string
	CurrentDispatcherContractVersion   int
	MinimumDispatcherVersion           string
	PackagePinCliVersion               string
	PackagePinMinimumDispatcherVersion string
	ProjectPinCliVersion               string
	ProjectPinMinimumDispatcherVersion string
}

type dispatcherMinimumVersionContract struct {
	CliVersion                string `json:"cliVersion"`
	DispatcherContractVersion int    `json:"dispatcherContractVersion"`
}

type dispatcherMinimumVersionReleaseContract struct {
	CliVersion                string           `json:"cliVersion"`
	DispatcherContractVersion *json.RawMessage `json:"dispatcherContractVersion"`
}

type dispatcherMinimumVersionCliPin struct {
	SchemaVersion            int    `json:"schemaVersion"`
	PackageName              string `json:"packageName"`
	PackageVersion           string `json:"packageVersion"`
	CliVersion               string `json:"cliVersion"`
	RequiredProtocolVersion  int    `json:"requiredProtocolVersion"`
	MinimumDispatcherVersion string `json:"minimumDispatcherVersion"`
}

type dispatcherMinimumVersionSyncableError struct {
	message string
}

func (err dispatcherMinimumVersionSyncableError) Error() string {
	return err.message
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
	contractContent, err := dispatcherMinimumVersionFileAtRef(ctx, repoRoot, ref, dispatcherContractFile)
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
		[]byte(contractContent),
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
	contractContent []byte,
	constantsContent []byte,
	packagePinContent []byte,
	projectPinContent []byte,
) (dispatcherMinimumVersionValues, error) {
	contract, err := parseDispatcherMinimumVersionContract(contractContent)
	if err != nil {
		return dispatcherMinimumVersionValues{}, err
	}
	constants, err := ParseProtocolMinimumVersionValues(constantsContent)
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
		CurrentCliVersion:                  contract.CliVersion,
		CurrentDispatcherContractVersion:   contract.DispatcherContractVersion,
		MinimumDispatcherVersion:           constants.MinimumCliVersion,
		PackagePinCliVersion:               packagePin.CliVersion,
		PackagePinMinimumDispatcherVersion: packagePin.MinimumDispatcherVersion,
		ProjectPinCliVersion:               projectPin.CliVersion,
		ProjectPinMinimumDispatcherVersion: projectPin.MinimumDispatcherVersion,
	}
	return values, validateDispatcherMinimumVersionValues(values)
}

func parseDispatcherMinimumVersionContract(content []byte) (dispatcherMinimumVersionContract, error) {
	contract := dispatcherMinimumVersionContract{}
	if err := json.Unmarshal(content, &contract); err != nil {
		return dispatcherMinimumVersionContract{}, fmt.Errorf("%s is invalid JSON: %w", dispatcherContractFile, err)
	}
	if contract.CliVersion == "" {
		return dispatcherMinimumVersionContract{}, fmt.Errorf("%s does not define cliVersion", dispatcherContractFile)
	}
	if contract.DispatcherContractVersion < minimumDispatcherContractVersion {
		return dispatcherMinimumVersionContract{}, dispatcherContractVersionTooLowError(
			dispatcherContractFile,
			contract.DispatcherContractVersion)
	}
	return contract, nil
}

func parseDispatcherMinimumVersionPin(path string, content []byte) (dispatcherMinimumVersionCliPin, error) {
	pin := dispatcherMinimumVersionCliPin{}
	if err := json.Unmarshal(content, &pin); err != nil {
		return dispatcherMinimumVersionCliPin{}, fmt.Errorf("%s is invalid JSON: %w", path, err)
	}
	if pin.CliVersion == "" {
		return dispatcherMinimumVersionCliPin{}, fmt.Errorf("%s does not define cliVersion", path)
	}
	if pin.MinimumDispatcherVersion == "" {
		return dispatcherMinimumVersionCliPin{}, fmt.Errorf("%s does not define %s", path, minimumDispatcherVersionDescription)
	}
	return pin, nil
}

func validateDispatcherMinimumVersionValues(values dispatcherMinimumVersionValues) error {
	if values.PackagePinCliVersion != values.CurrentCliVersion {
		return fmt.Errorf("%s cliVersion %q does not match %s cliVersion %q",
			unityPackageCliPinFile,
			values.PackagePinCliVersion,
			dispatcherContractFile,
			values.CurrentCliVersion)
	}
	if values.ProjectPinCliVersion != values.PackagePinCliVersion {
		return fmt.Errorf("%s cliVersion %q does not match %s cliVersion %q",
			unityProjectCliPinFile,
			values.ProjectPinCliVersion,
			unityPackageCliPinFile,
			values.PackagePinCliVersion)
	}
	if values.PackagePinMinimumDispatcherVersion != values.MinimumDispatcherVersion {
		return fmt.Errorf("%s %s %q does not match %s MINIMUM_REQUIRED_CLI_VERSION %q",
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
	if values.MinimumDispatcherVersion == values.CurrentCliVersion {
		return verifyCurrentDispatcherMinimumVersion(values)
	}

	releaseTag := cliReleaseTagPrefix + values.MinimumDispatcherVersion
	contractContent, err := protocolMinimumVersionFileAtRef(ctx, repoRoot, releaseTag, dispatcherContractFile)
	if err != nil {
		return fmt.Errorf("CLI release %s does not provide %s", releaseTag, dispatcherContractFile)
	}
	return verifyMinimumCliReleaseDispatcherContract(values, []byte(contractContent))
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
		return fmt.Errorf("CLI release contract is invalid JSON: %w", err)
	}
	if contract.CliVersion != "" && contract.CliVersion != values.MinimumDispatcherVersion {
		return fmt.Errorf(
			"CLI release %s%s contract declares cliVersion %q",
			cliReleaseTagPrefix,
			values.MinimumDispatcherVersion,
			contract.CliVersion)
	}

	releaseLabel := cliReleaseTagPrefix + values.MinimumDispatcherVersion
	dispatcherContractVersion, hasDispatcherContractVersion, err := dispatcherMinimumReleaseContractVersion(
		releaseLabel,
		contract.DispatcherContractVersion)
	if err != nil {
		return err
	}
	if !hasDispatcherContractVersion {
		return dispatcherMinimumVersionSyncableError{
			message: fmt.Sprintf("CLI release %s does not define dispatcherContractVersion", releaseLabel),
		}
	}
	if dispatcherContractVersion < minimumDispatcherContractVersion {
		return dispatcherContractVersionTooLowError("CLI release "+releaseLabel, dispatcherContractVersion)
	}
	if dispatcherContractVersion < values.CurrentDispatcherContractVersion {
		return dispatcherMinimumVersionSyncableError{
			message: fmt.Sprintf(
				"unity package requires dispatcher contract %d, but CLI release %s advertises dispatcher contract %d",
				values.CurrentDispatcherContractVersion,
				releaseLabel,
				dispatcherContractVersion),
		}
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
			"CLI release %s dispatcherContractVersion must be an integer, got %s",
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

func isDispatcherMinimumVersionSyncableError(err error) bool {
	syncableError := dispatcherMinimumVersionSyncableError{}
	return errors.As(err, &syncableError)
}

func syncDispatcherMinimumVersionFiles(repoRoot string, targetVersion string) (bool, error) {
	changedConstants, err := syncDispatcherMinimumVersionConstants(repoRoot, targetVersion)
	if err != nil {
		return false, err
	}
	changedPackagePin, err := syncDispatcherMinimumVersionPin(filepath.Join(repoRoot, unityPackageCliPinFile), targetVersion)
	if err != nil {
		return false, err
	}
	changedProjectPin, err := syncDispatcherMinimumVersionPin(filepath.Join(repoRoot, unityProjectCliPinFile), targetVersion)
	if err != nil {
		return false, err
	}
	return changedConstants || changedPackagePin || changedProjectPin, nil
}

func syncDispatcherMinimumVersionConstants(repoRoot string, targetVersion string) (bool, error) {
	path := filepath.Join(repoRoot, protocolMinimumVersionFile)
	content, err := os.ReadFile(path)
	if err != nil {
		return false, err
	}

	updatedContent := minimumCliVersionPattern.ReplaceAll(
		content,
		[]byte(`MINIMUM_REQUIRED_CLI_VERSION = "`+targetVersion+`"`))
	if bytes.Equal(content, updatedContent) {
		return false, nil
	}
	if err := os.WriteFile(path, updatedContent, 0o644); err != nil {
		return false, err
	}
	return true, nil
}

func syncDispatcherMinimumVersionPin(path string, targetVersion string) (bool, error) {
	content, err := os.ReadFile(path)
	if err != nil {
		return false, err
	}

	fields := map[string]json.RawMessage{}
	if err := json.Unmarshal(content, &fields); err != nil {
		return false, fmt.Errorf("%s is invalid JSON: %w", path, err)
	}
	currentVersion, err := dispatcherMinimumVersionStringField(path, fields, minimumDispatcherVersionDescription)
	if err != nil {
		return false, err
	}
	if currentVersion == targetVersion {
		return false, nil
	}

	updatedValue, err := json.Marshal(targetVersion)
	if err != nil {
		return false, err
	}
	fields[minimumDispatcherVersionDescription] = json.RawMessage(updatedValue)
	updatedContent, err := json.MarshalIndent(fields, "", "  ")
	if err != nil {
		return false, err
	}
	updatedContent = append(updatedContent, '\n')
	if err := os.WriteFile(path, updatedContent, 0o644); err != nil {
		return false, err
	}
	return true, nil
}

func dispatcherMinimumVersionStringField(
	path string,
	fields map[string]json.RawMessage,
	fieldName string,
) (string, error) {
	rawValue, ok := fields[fieldName]
	if !ok {
		return "", fmt.Errorf("%s does not define %s", path, fieldName)
	}

	value := ""
	if err := json.Unmarshal(rawValue, &value); err != nil {
		return "", fmt.Errorf("%s %s must be a string: %w", path, fieldName, err)
	}
	if value == "" {
		return "", fmt.Errorf("%s does not define %s", path, fieldName)
	}
	return value, nil
}

func writeDispatcherMinimumVersionLine(writer io.Writer, values ...any) {
	_, _ = fmt.Fprintln(writer, values...)
}
