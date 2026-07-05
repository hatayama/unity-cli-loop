package automation

import (
	"context"
	"encoding/json"
	"fmt"
	"io"
	"os"
	"path/filepath"
	"regexp"
	"strings"
)

// Contract file paths are read from historical tags as well as HEAD, and each
// directory move creates a new generation of paths that must all continue to
// resolve. Four generations are currently live for release-tag reads:
//
//  1. Current layout: the dispatcher contract package lives under
//     `cli/dispatcher/dispatchercontract/`, matching `common/clicontract`.
//  2. Initial `cli/` module layout: Go modules lived under `cli/`, but the
//     dispatcher contract file still sat at the dispatcher module root.
//  3. Root-modules layout: contract files sat at `common/...` and
//     `dispatcher/...` at the repo root. Tags published between PR #1461 (v2->v3
//     module split) and this move still resolve their contract at these paths.
//  4. Pre-split single-module layout (v2): all files lived under a top-level
//     `cli/` module with flat filenames. These are the oldest paths.
const (
	// Primary paths (current layout, after moving Go modules under `cli/`).
	cliContractFile        = "cli/common/clicontract/contract.json"
	dispatcherContractFile = "cli/dispatcher/dispatchercontract/dispatcher-contract.json"
	// Previous cli/dispatcher layout.
	cliDispatcherRootContractFile = "cli/dispatcher/dispatcher-contract.json"
	// Middle-generation paths (root-modules layout, between PR #1461 and this move).
	rootModulesRunnerContractFile     = "common/clicontract/contract.json"
	rootModulesDispatcherContractFile = "dispatcher/dispatcher-contract.json"
	// Oldest paths (pre-split v2 single-module `cli/` era).
	legacyRunnerContractFile     = "cli/contract.json"
	legacyDispatcherContractFile = "cli/dispatcher-contract.json"

	dispatcherReleaseTagPrefix          = "dispatcher-v"
	unityPackageCliPinFile              = "Packages/src/project-runner-pin.json"
	unityProjectCliPinFile              = ".uloop/project-runner-pin.json"
	minimumDispatcherVersionDescription = "minimumDispatcherVersion"
)

// Each side's contract path chain, newest generation first. Every consumer
// (fallback reads, existence probes, operator-facing messages) derives from
// these slices so the next directory move adds its generation in exactly one
// place per side instead of at every call site.
var (
	runnerContractPathChain     = []string{cliContractFile, rootModulesRunnerContractFile, legacyRunnerContractFile}
	dispatcherContractPathChain = []string{dispatcherContractFile, cliDispatcherRootContractFile, rootModulesDispatcherContractFile, legacyDispatcherContractFile}
)

var minimumDispatcherVersionPattern = regexp.MustCompile(`MINIMUM_REQUIRED_DISPATCHER_VERSION\s*=\s*"([^"]+)"`)

type dispatcherMinimumVersionValues struct {
	CurrentProjectRunnerVersion        string
	CurrentDispatcherVersion           string
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
	DispatcherVersion string `json:"dispatcherVersion"`
}

type dispatcherMinimumVersionReleaseContract struct {
	DispatcherVersion string `json:"dispatcherVersion"`
}

type dispatcherMinimumVersionCliPin struct {
	ProjectRunnerVersion     string `json:"projectRunnerVersion"`
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
		return nil
	}

	releaseTag := dispatcherReleaseTagPrefix + values.MinimumDispatcherVersion
	contractContent, err := dispatcherContractFileAtRef(ctx, repoRoot, releaseTag)
	if err != nil {
		return fmt.Errorf(
			"dispatcher release %s does not provide %s",
			releaseTag,
			strings.Join(dispatcherContractPathChain, " or "))
	}
	return verifyMinimumCliReleaseDispatcherContract(values, []byte(contractContent))
}

// dispatcherContractFileAtRef reads the dispatcher release contract at a git ref.
// The fallback chain (primary -> root-modules -> pre-split) covers every generation
// of dispatcher release tags currently live in the repository.
func dispatcherContractFileAtRef(ctx context.Context, repoRoot string, ref string) (string, error) {
	return contractFileAtRefWithLegacyFallback(
		ctx,
		repoRoot,
		ref,
		dispatcherContractPathChain[0],
		dispatcherContractPathChain[1:]...)
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
	return nil
}

func writeDispatcherMinimumVersionLine(writer io.Writer, values ...any) {
	_, _ = fmt.Fprintln(writer, values...)
}
