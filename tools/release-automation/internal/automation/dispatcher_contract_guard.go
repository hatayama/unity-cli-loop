package automation

import (
	"context"
	"encoding/json"
	"fmt"
	"io"
	"strconv"
	"strings"
)

type DispatcherContractGuardConfig struct {
	BaseRef string
	HeadRef string
}

type DispatcherContractValues struct {
	HasContract               bool
	DispatcherContractVersion int
}

type DispatcherContractGuardResult struct {
	Base                               DispatcherContractValues
	Head                               DispatcherContractValues
	DispatcherContractVersionDecreased bool
}

type dispatcherContractDocument struct {
	DispatcherContractVersion int `json:"dispatcherContractVersion"`
}

func RunDispatcherContractGuard(
	ctx context.Context,
	stdout io.Writer,
	stderr io.Writer,
	config DispatcherContractGuardConfig,
) int {
	result, err := AnalyzeDispatcherContractGuardForRefs(ctx, config)
	if err != nil {
		writeDispatcherContractLine(stderr, err)
		return 1
	}
	if dispatcherContractGuardNeedsAction(result) {
		writeDispatcherContractLine(stderr, FormatDispatcherContractWarning(result))
		return 1
	}

	writeDispatcherContractLine(stdout, "Dispatcher contract guard passed.")
	return 0
}

func AnalyzeDispatcherContractGuardForRefs(
	ctx context.Context,
	config DispatcherContractGuardConfig,
) (DispatcherContractGuardResult, error) {
	if config.BaseRef == "" {
		return DispatcherContractGuardResult{}, fmt.Errorf("--base is required")
	}
	if config.HeadRef == "" {
		config.HeadRef = "HEAD"
	}

	repoRoot, err := gitRepoRoot(ctx)
	if err != nil {
		return DispatcherContractGuardResult{}, fmt.Errorf("failed to resolve git repository root: %w", err)
	}

	headValues, err := dispatcherContractValuesAtRef(ctx, repoRoot, config.HeadRef)
	if err != nil {
		return DispatcherContractGuardResult{}, fmt.Errorf("failed to read head %s: %w", dispatcherContractFile, err)
	}

	// The base ref may predate the directory split, where the contract lived at
	// the legacy cli/ path, or predate the contract entirely. Both cases are an
	// initial introduction, not a regression.
	baseContent, baseContentErr := dispatcherContractFileAtRef(ctx, repoRoot, config.BaseRef)
	baseValues, err := parseDispatcherContractBaseValues(ctx, repoRoot, config.BaseRef, baseContent, baseContentErr)
	if err != nil {
		return DispatcherContractGuardResult{}, err
	}

	return AnalyzeDispatcherContractGuard(baseValues, headValues), nil
}

func AnalyzeDispatcherContractGuard(
	base DispatcherContractValues,
	head DispatcherContractValues,
) DispatcherContractGuardResult {
	return DispatcherContractGuardResult{
		Base: base,
		Head: head,
		DispatcherContractVersionDecreased: base.HasContract &&
			head.HasContract &&
			head.DispatcherContractVersion < base.DispatcherContractVersion,
	}
}

func dispatcherContractGuardNeedsAction(result DispatcherContractGuardResult) bool {
	return result.DispatcherContractVersionDecreased
}

func parseDispatcherContractBaseValues(
	ctx context.Context,
	repoRoot string,
	baseRef string,
	content string,
	readErr error,
) (DispatcherContractValues, error) {
	if readErr != nil {
		missing, guardErr := isMissingDispatcherContractAtRefError(ctx, repoRoot, baseRef)
		if guardErr != nil {
			return DispatcherContractValues{}, fmt.Errorf("failed to classify base %s read failure: %w", dispatcherContractFile, guardErr)
		}
		if missing {
			return DispatcherContractValues{}, nil
		}
		return DispatcherContractValues{}, fmt.Errorf("failed to read base %s: %w", dispatcherContractFile, readErr)
	}

	values, err := ParseDispatcherContractValues([]byte(content))
	if err != nil {
		return DispatcherContractValues{}, fmt.Errorf("failed to parse base %s: %w", dispatcherContractFile, err)
	}
	return values, nil
}

// isMissingDispatcherContractAtRefError reports whether the base ref exists yet
// carries neither the current dispatcher contract path nor its legacy
// counterpart. Only that combination is an initial introduction; a missing
// ref, or any git execution failure, must propagate instead of being silently
// treated as "no contract".
func isMissingDispatcherContractAtRefError(
	ctx context.Context,
	repoRoot string,
	baseRef string,
) (bool, error) {
	refExists, err := refExistsAtRef(ctx, repoRoot, baseRef)
	if err != nil {
		return false, err
	}
	if !refExists {
		return false, nil
	}
	primaryExists, err := fileExistsAtRef(ctx, repoRoot, baseRef, dispatcherContractFile)
	if err != nil {
		return false, err
	}
	if primaryExists {
		return false, nil
	}
	legacyExists, err := fileExistsAtRef(ctx, repoRoot, baseRef, legacyDispatcherContractFile)
	if err != nil {
		return false, err
	}
	return !legacyExists, nil
}

// ParseDispatcherContractValues extracts only what the guard compares.
// dispatcherVersion is intentionally not read or validated here: the guard
// never consumes it, and the dispatcher module's own contract tests pin its
// semver format on every PR.
func ParseDispatcherContractValues(content []byte) (DispatcherContractValues, error) {
	contract := dispatcherContractDocument{}
	if err := json.Unmarshal(content, &contract); err != nil {
		return DispatcherContractValues{}, fmt.Errorf("%s is invalid JSON: %w", dispatcherContractFile, err)
	}

	if contract.DispatcherContractVersion < 1 {
		return DispatcherContractValues{}, fmt.Errorf(
			"%s dispatcherContractVersion must be at least 1, got %s",
			dispatcherContractFile,
			strconv.Itoa(contract.DispatcherContractVersion))
	}

	return DispatcherContractValues{
		HasContract:               true,
		DispatcherContractVersion: contract.DispatcherContractVersion,
	}, nil
}

func FormatDispatcherContractWarning(result DispatcherContractGuardResult) string {
	builder := strings.Builder{}
	builder.WriteString("dispatcherContractVersion moved backwards.\n\n")
	builder.WriteString("- Base dispatcher contract version: `")
	builder.WriteString(strconv.Itoa(result.Base.DispatcherContractVersion))
	builder.WriteString("`\n")
	builder.WriteString("- Head dispatcher contract version: `")
	builder.WriteString(strconv.Itoa(result.Head.DispatcherContractVersion))
	builder.WriteString("`\n\n")
	builder.WriteString("Dispatcher contract generations only move forward. Restore the base value or bump it when the dispatcher contract itself changes.")
	return builder.String()
}

func dispatcherContractValuesAtRef(
	ctx context.Context,
	repoRoot string,
	ref string,
) (DispatcherContractValues, error) {
	content, err := protocolMinimumVersionFileAtRef(ctx, repoRoot, ref, dispatcherContractFile)
	if err != nil {
		return DispatcherContractValues{}, err
	}
	return ParseDispatcherContractValues([]byte(content))
}

func writeDispatcherContractLine(writer io.Writer, values ...any) {
	_, _ = fmt.Fprintln(writer, values...)
}
