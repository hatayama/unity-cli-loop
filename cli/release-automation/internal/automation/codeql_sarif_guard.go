package automation

import (
	"encoding/json"
	"fmt"
	"os"
	"regexp"
)

const (
	expectedCodeQLSARIFSchema         = "https://json.schemastore.org/sarif-2.1.0.json"
	expectedCodeQLSARIFVersion        = "2.1.0"
	expectedCodeQLToolName            = "CodeQL"
	expectedCodeQLToolSemanticVersion = "2.26.0"
	// These hard floors detect extractor collapse without rejecting normal Unity-source composition changes.
	minimumCodeQLCallTargetPercentage = 55
	minimumCodeQLKnownTypePercentage  = 70
	// These warning baselines record the 2026-07-15 no-build PoC so quality drift remains visible without blocking valid source changes.
	baselineCodeQLCallTargetPercentage = 68
	baselineCodeQLKnownTypePercentage  = 82
	// This floor detects a hollowed database while allowing expected source movement from the 119122-line PoC.
	minimumCodeQLLinesOfCode              = 75000
	codeQLBuildlessCompletionMessage      = "C# analysis with build-mode 'none' completed."
	codeQLBuildlessCompletionDiagnosticID = "csharp/autobuilder/buildless/complete"
	codeQLQualityDiagnosticID             = "csharp/diagnostic/database-quality"
	codeQLExtractedFilesDiagnosticID      = "cs/diagnostics/successfully-extracted-files"
	codeQLLinesOfCodeMetricID             = "cs/summary/lines-of-code"
)

var codeQLQualityMetricPattern = regexp.MustCompile(`Percentage of calls with call target: ([0-9]+) % .*Percentage of expressions with known type: ([0-9]+) %`)

type codeQLSARIFReport struct {
	Schema  string           `json:"$schema"`
	Version string           `json:"version"`
	Runs    []codeQLSARIFRun `json:"runs"`
}

type codeQLSARIFRun struct {
	Tool        codeQLSARIFTool         `json:"tool"`
	Invocations []codeQLSARIFInvocation `json:"invocations"`
	Properties  codeQLSARIFProperties   `json:"properties"`
	Results     []json.RawMessage       `json:"results"`
}

type codeQLSARIFTool struct {
	Driver codeQLSARIFToolDriver `json:"driver"`
}

type codeQLSARIFToolDriver struct {
	Name            string `json:"name"`
	SemanticVersion string `json:"semanticVersion"`
}

type codeQLSARIFInvocation struct {
	ExecutionSuccessful        bool                      `json:"executionSuccessful"`
	ToolExecutionNotifications []codeQLSARIFNotification `json:"toolExecutionNotifications"`
}

type codeQLSARIFNotification struct {
	Descriptor codeQLSARIFDescriptor `json:"descriptor"`
	Message    codeQLSARIFMessage    `json:"message"`
}

type codeQLSARIFDescriptor struct {
	ID string `json:"id"`
}

type codeQLSARIFMessage struct {
	Text string `json:"text"`
}

type codeQLSARIFProperties struct {
	MetricResults []codeQLSARIFMetricResult `json:"metricResults"`
}

type codeQLSARIFMetricResult struct {
	RuleID string  `json:"ruleId"`
	Value  float64 `json:"value"`
}

// CodeQLSARIFValidationResult carries nonblocking database-quality drift warnings.
type CodeQLSARIFValidationResult struct {
	Warnings []string
}

// ValidateCodeQLSARIFFile reads and validates a CodeQL SARIF report before upload.
func ValidateCodeQLSARIFFile(path string) (CodeQLSARIFValidationResult, error) {
	data, err := os.ReadFile(path)
	if err != nil {
		return CodeQLSARIFValidationResult{}, fmt.Errorf("read CodeQL SARIF: %w", err)
	}
	return validateCodeQLSARIF(data)
}

// ValidateCodeQLSARIF validates a completed, sufficiently complete CodeQL C# no-build analysis before upload.
func ValidateCodeQLSARIF(data []byte) error {
	_, err := validateCodeQLSARIF(data)
	return err
}

func validateCodeQLSARIF(data []byte) (CodeQLSARIFValidationResult, error) {
	report := codeQLSARIFReport{}
	if err := json.Unmarshal(data, &report); err != nil {
		return CodeQLSARIFValidationResult{}, fmt.Errorf("invalid SARIF JSON: %w", err)
	}
	if report.Schema != expectedCodeQLSARIFSchema || report.Version != expectedCodeQLSARIFVersion {
		return CodeQLSARIFValidationResult{}, fmt.Errorf("expected SARIF schema %q and version %q", expectedCodeQLSARIFSchema, expectedCodeQLSARIFVersion)
	}
	if len(report.Runs) != 1 {
		return CodeQLSARIFValidationResult{}, fmt.Errorf("expected exactly one SARIF run, got %d", len(report.Runs))
	}
	run := report.Runs[0]
	if run.Tool.Driver.Name != expectedCodeQLToolName {
		return CodeQLSARIFValidationResult{}, fmt.Errorf("expected SARIF tool %q, got %q", expectedCodeQLToolName, run.Tool.Driver.Name)
	}
	if run.Tool.Driver.SemanticVersion != expectedCodeQLToolSemanticVersion {
		return CodeQLSARIFValidationResult{}, fmt.Errorf("expected CodeQL version %q, got %q", expectedCodeQLToolSemanticVersion, run.Tool.Driver.SemanticVersion)
	}
	if len(run.Invocations) != 1 || !run.Invocations[0].ExecutionSuccessful {
		return CodeQLSARIFValidationResult{}, fmt.Errorf("CodeQL invocation did not complete successfully")
	}
	return validateCodeQLSARIFQuality(run)
}

func validateCodeQLSARIFQuality(run codeQLSARIFRun) (CodeQLSARIFValidationResult, error) {
	invocation := run.Invocations[0]
	hasBuildlessCompletion := false
	extractedFileCount := 0
	// CodeQL 2.26.0 emits the database-quality diagnostic only below its own 85% thresholds. The strict semantic-version check above freezes that behavior, so absence means both metrics are at least 85%, not that evidence was silently lost.
	callTargets := 100
	knownTypes := 100
	for _, notification := range invocation.ToolExecutionNotifications {
		if notification.Descriptor.ID == codeQLBuildlessCompletionDiagnosticID && notification.Message.Text == codeQLBuildlessCompletionMessage {
			hasBuildlessCompletion = true
		}
		if notification.Descriptor.ID == codeQLExtractedFilesDiagnosticID {
			extractedFileCount++
		}
		if notification.Descriptor.ID == codeQLQualityDiagnosticID {
			parsedCallTargets, parsedKnownTypes, err := parseCodeQLQualityMetrics(notification.Message.Text)
			if err != nil {
				return CodeQLSARIFValidationResult{}, err
			}
			callTargets = parsedCallTargets
			knownTypes = parsedKnownTypes
		}
	}
	if !hasBuildlessCompletion {
		return CodeQLSARIFValidationResult{}, fmt.Errorf("CodeQL SARIF is missing build-mode none completion diagnostic")
	}
	if extractedFileCount == 0 {
		return CodeQLSARIFValidationResult{}, fmt.Errorf("CodeQL SARIF has no successfully extracted C# files")
	}
	if callTargets < minimumCodeQLCallTargetPercentage || knownTypes < minimumCodeQLKnownTypePercentage {
		return CodeQLSARIFValidationResult{}, fmt.Errorf("CodeQL database quality is below the approved floor: call targets %d%%, known types %d%%", callTargets, knownTypes)
	}
	linesOfCode := float64(0)
	for _, metric := range run.Properties.MetricResults {
		if metric.RuleID == codeQLLinesOfCodeMetricID {
			linesOfCode = metric.Value
		}
	}
	if linesOfCode < float64(minimumCodeQLLinesOfCode) {
		return CodeQLSARIFValidationResult{}, fmt.Errorf("CodeQL extracted lines of code %.0f is below the approved floor %d", linesOfCode, minimumCodeQLLinesOfCode)
	}
	warnings := []string{}
	if callTargets < baselineCodeQLCallTargetPercentage || knownTypes < baselineCodeQLKnownTypePercentage {
		warnings = append(warnings, fmt.Sprintf("CodeQL database quality is below the 2026-07-15 PoC baseline: call targets %d%% (baseline %d%%), known types %d%% (baseline %d%%)", callTargets, baselineCodeQLCallTargetPercentage, knownTypes, baselineCodeQLKnownTypePercentage))
	}
	return CodeQLSARIFValidationResult{Warnings: warnings}, nil
}

func parseCodeQLQualityMetrics(message string) (int, int, error) {
	matches := codeQLQualityMetricPattern.FindStringSubmatch(message)
	if len(matches) != 3 {
		return 0, 0, fmt.Errorf("CodeQL database quality diagnostic has an unsupported format")
	}
	callTargets := 0
	knownTypes := 0
	if _, err := fmt.Sscanf(matches[1], "%d", &callTargets); err != nil {
		return 0, 0, fmt.Errorf("parse CodeQL call target quality: %w", err)
	}
	if _, err := fmt.Sscanf(matches[2], "%d", &knownTypes); err != nil {
		return 0, 0, fmt.Errorf("parse CodeQL known type quality: %w", err)
	}
	return callTargets, knownTypes, nil
}
