package automation

import (
	"context"
	"encoding/json"
	"fmt"
	"io"
	"path"
	"sort"
	"strconv"
	"strings"

	sharedversion "github.com/hatayama/unity-cli-loop/common/version"
)

var dispatcherReleaseInputPatterns = []string{
	"dispatcher/cmd/dispatcher/main.go",
	"dispatcher/contract.go",
	dispatcherContractFile,
	"common/clicore/*.go",
	"dispatcher/internal/dispatcher/*.go",
	"dispatcher/internal/install/*.go",
	"dispatcher/internal/uninstall/*.go",
	"dispatcher/internal/update/*.go",
	"scripts/install.ps1",
	"scripts/install.sh",
}

type DispatcherVersionBumpGuardConfig struct {
	BaseRef string
	HeadRef string
}

type DispatcherVersionBumpValues struct {
	HasContract               bool
	DispatcherVersion         string
	DispatcherContractVersion int
}

type DispatcherVersionBumpGuardResult struct {
	ChangedDispatcherInputs            []string
	Base                               DispatcherVersionBumpValues
	Head                               DispatcherVersionBumpValues
	DispatcherVersionIncreased         bool
	DispatcherContractVersionDecreased bool
}

type dispatcherVersionBumpContract struct {
	DispatcherVersion         string `json:"dispatcherVersion"`
	DispatcherContractVersion int    `json:"dispatcherContractVersion"`
}

func RunDispatcherVersionBumpGuard(
	ctx context.Context,
	stdout io.Writer,
	stderr io.Writer,
	config DispatcherVersionBumpGuardConfig,
) int {
	result, err := AnalyzeDispatcherVersionBumpGuardForRefs(ctx, config)
	if err != nil {
		writeDispatcherVersionBumpLine(stderr, err)
		return 1
	}
	if dispatcherVersionBumpGuardNeedsAction(result) {
		writeDispatcherVersionBumpLine(stderr, FormatDispatcherVersionBumpWarning(result))
		return 1
	}

	writeDispatcherVersionBumpLine(stdout, "Dispatcher version bump guard passed.")
	return 0
}

func AnalyzeDispatcherVersionBumpGuardForRefs(
	ctx context.Context,
	config DispatcherVersionBumpGuardConfig,
) (DispatcherVersionBumpGuardResult, error) {
	if config.BaseRef == "" {
		return DispatcherVersionBumpGuardResult{}, fmt.Errorf("--base is required")
	}
	if config.HeadRef == "" {
		config.HeadRef = "HEAD"
	}

	repoRoot, err := gitRepoRoot(ctx)
	if err != nil {
		return DispatcherVersionBumpGuardResult{}, fmt.Errorf("failed to resolve git repository root: %w", err)
	}

	changedFiles, err := gitChangedFiles(ctx, repoRoot, config.BaseRef, config.HeadRef)
	if err != nil {
		return DispatcherVersionBumpGuardResult{}, fmt.Errorf("failed to inspect changed files: %w", err)
	}

	headValues, err := dispatcherVersionBumpValuesAtRef(ctx, repoRoot, config.HeadRef)
	if err != nil {
		return DispatcherVersionBumpGuardResult{}, fmt.Errorf("failed to read head %s: %w", dispatcherContractFile, err)
	}

	// The base ref may predate the directory split, where the contract lived at
	// the legacy cli/ path. Without the fallback every pre-split base would look
	// like an initial contract introduction and the bump requirement would be
	// silently skipped.
	baseContent, baseContentErr := dispatcherContractFileAtRef(ctx, repoRoot, config.BaseRef)
	baseValues, err := parseDispatcherVersionBumpBaseValues(baseContent, baseContentErr)
	if err != nil {
		return DispatcherVersionBumpGuardResult{}, err
	}

	return AnalyzeDispatcherVersionBumpGuard(changedFiles, baseValues, headValues), nil
}

func parseDispatcherVersionBumpBaseValues(
	content string,
	readErr error,
) (DispatcherVersionBumpValues, error) {
	if readErr != nil {
		if isMissingDispatcherContractAtRefError(readErr) {
			return DispatcherVersionBumpValues{}, nil
		}
		return DispatcherVersionBumpValues{}, fmt.Errorf("failed to read base %s: %w", dispatcherContractFile, readErr)
	}

	values, err := ParseDispatcherVersionBumpValues([]byte(content))
	if err != nil {
		return DispatcherVersionBumpValues{}, fmt.Errorf("failed to parse base %s: %w", dispatcherContractFile, err)
	}
	return values, nil
}

func isMissingDispatcherContractAtRefError(err error) bool {
	// dispatcherContractFileAtRef falls back to the legacy path, so a base ref
	// without any dispatcher contract surfaces as the legacy file missing.
	return isMissingFileAtRefError(err, dispatcherContractFile) ||
		isMissingFileAtRefError(err, legacyDispatcherContractFile)
}

func AnalyzeDispatcherVersionBumpGuard(
	changedFiles []string,
	base DispatcherVersionBumpValues,
	head DispatcherVersionBumpValues,
) DispatcherVersionBumpGuardResult {
	versionComparison := 0
	versionComparisonValid := false
	if base.HasContract && head.HasContract {
		versionComparison, versionComparisonValid = sharedversion.Compare(head.DispatcherVersion, base.DispatcherVersion)
	}

	return DispatcherVersionBumpGuardResult{
		ChangedDispatcherInputs:    changedDispatcherReleaseInputs(changedFiles),
		Base:                       base,
		Head:                       head,
		DispatcherVersionIncreased: versionComparisonValid && versionComparison > 0,
		DispatcherContractVersionDecreased: base.HasContract &&
			head.HasContract &&
			head.DispatcherContractVersion < base.DispatcherContractVersion,
	}
}

func ParseDispatcherVersionBumpValues(content []byte) (DispatcherVersionBumpValues, error) {
	contract := dispatcherVersionBumpContract{}
	if err := json.Unmarshal(content, &contract); err != nil {
		return DispatcherVersionBumpValues{}, fmt.Errorf("%s is invalid JSON: %w", dispatcherContractFile, err)
	}

	dispatcherVersion := strings.TrimSpace(contract.DispatcherVersion)
	if dispatcherVersion == "" {
		return DispatcherVersionBumpValues{}, fmt.Errorf("%s does not define dispatcherVersion", dispatcherContractFile)
	}
	if _, ok := sharedversion.Compare(dispatcherVersion, dispatcherVersion); !ok {
		return DispatcherVersionBumpValues{}, fmt.Errorf("%s dispatcherVersion must be semver, got %q", dispatcherContractFile, dispatcherVersion)
	}
	if contract.DispatcherContractVersion < 1 {
		return DispatcherVersionBumpValues{}, fmt.Errorf(
			"%s dispatcherContractVersion must be at least 1, got %s",
			dispatcherContractFile,
			strconv.Itoa(contract.DispatcherContractVersion))
	}

	return DispatcherVersionBumpValues{
		HasContract:               true,
		DispatcherVersion:         dispatcherVersion,
		DispatcherContractVersion: contract.DispatcherContractVersion,
	}, nil
}

func FormatDispatcherVersionBumpWarning(result DispatcherVersionBumpGuardResult) string {
	builder := strings.Builder{}
	if result.DispatcherContractVersionDecreased {
		builder.WriteString("Dispatcher release inputs changed, but dispatcherContractVersion moved backwards.\n\n")
	} else {
		builder.WriteString("Dispatcher release inputs changed, but dispatcherVersion did not increase.\n\n")
	}
	builder.WriteString("- Base dispatcher version: ")
	builder.WriteString(dispatcherVersionBumpVersionLabel(result.Base))
	builder.WriteString("\n")
	builder.WriteString("- Head dispatcher version: ")
	builder.WriteString(dispatcherVersionBumpVersionLabel(result.Head))
	builder.WriteString("\n")
	builder.WriteString("- Base dispatcher contract version: ")
	builder.WriteString(dispatcherVersionBumpContractVersionLabel(result.Base))
	builder.WriteString("\n")
	builder.WriteString("- Head dispatcher contract version: ")
	builder.WriteString(dispatcherVersionBumpContractVersionLabel(result.Head))
	builder.WriteString("\n\n")
	builder.WriteString("Changed dispatcher release inputs:\n")
	for _, changedInput := range result.ChangedDispatcherInputs {
		builder.WriteString("- `")
		builder.WriteString(changedInput)
		builder.WriteString("`\n")
	}
	builder.WriteString("\nUpdate `dispatcher/dispatcher-contract.json` `dispatcherVersion` before merging dispatcher release changes.")
	return builder.String()
}

func dispatcherVersionBumpGuardNeedsAction(result DispatcherVersionBumpGuardResult) bool {
	if len(result.ChangedDispatcherInputs) == 0 {
		return false
	}
	if !result.Head.HasContract {
		return true
	}
	if !result.Base.HasContract {
		return false
	}
	if result.DispatcherContractVersionDecreased {
		return true
	}
	return !result.DispatcherVersionIncreased
}

func dispatcherVersionBumpValuesAtRef(
	ctx context.Context,
	repoRoot string,
	ref string,
) (DispatcherVersionBumpValues, error) {
	content, err := protocolMinimumVersionFileAtRef(ctx, repoRoot, ref, dispatcherContractFile)
	if err != nil {
		return DispatcherVersionBumpValues{}, err
	}
	return ParseDispatcherVersionBumpValues([]byte(content))
}

func changedDispatcherReleaseInputs(changedFiles []string) []string {
	changedInputs := []string{}
	for _, changedFile := range changedFiles {
		normalizedFile := normalizeDispatcherReleaseInputPath(changedFile)
		if normalizedFile == "" || strings.HasSuffix(normalizedFile, "_test.go") {
			continue
		}
		if matchesAnyDispatcherReleaseInputPattern(normalizedFile) {
			changedInputs = append(changedInputs, normalizedFile)
		}
	}
	sort.Strings(changedInputs)
	return changedInputs
}

func normalizeDispatcherReleaseInputPath(value string) string {
	normalizedValue := strings.ReplaceAll(strings.TrimSpace(value), "\\", "/")
	normalizedValue = strings.TrimPrefix(normalizedValue, "./")
	if normalizedValue == "" {
		return ""
	}
	return path.Clean(normalizedValue)
}

func matchesAnyDispatcherReleaseInputPattern(file string) bool {
	for _, pattern := range dispatcherReleaseInputPatterns {
		if matchesDispatcherReleaseInputPattern(file, pattern) {
			return true
		}
	}
	return false
}

func matchesDispatcherReleaseInputPattern(file string, pattern string) bool {
	ok, err := path.Match(pattern, file)
	if err != nil {
		return false
	}
	return ok
}

func dispatcherVersionBumpVersionLabel(values DispatcherVersionBumpValues) string {
	if !values.HasContract {
		return "`<missing>`"
	}
	return "`" + values.DispatcherVersion + "`"
}

func dispatcherVersionBumpContractVersionLabel(values DispatcherVersionBumpValues) string {
	if !values.HasContract {
		return "`<missing>`"
	}
	return "`" + strconv.Itoa(values.DispatcherContractVersion) + "`"
}

func writeDispatcherVersionBumpLine(writer io.Writer, values ...any) {
	_, _ = fmt.Fprintln(writer, values...)
}
