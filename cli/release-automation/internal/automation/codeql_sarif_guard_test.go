package automation

import (
	"os"
	"path/filepath"
	"strings"
	"testing"
)

func TestValidateCodeQLSARIFAcceptsNonEmptyFindingAtQualityBaseline(t *testing.T) {
	// Verifies a completed CodeQL security scan with a finding and the approved quality baseline is accepted.
	sarif := []byte(`{
	  "$schema":"https://json.schemastore.org/sarif-2.1.0.json",
	  "version":"2.1.0",
	  "runs":[{
	    "tool":{"driver":{"name":"CodeQL","semanticVersion":"2.26.0"}},
	    "properties":{"metricResults":[{"ruleId":"cs/summary/lines-of-code","value":75000}]},
	    "invocations":[{"executionSuccessful":true,"toolExecutionNotifications":[
	      {"descriptor":{"id":"csharp/autobuilder/buildless/complete"},"message":{"text":"C# analysis with build-mode 'none' completed."}},
	      {"descriptor":{"id":"csharp/diagnostic/database-quality"},"message":{"text":"Some metrics of the database quality are: Percentage of calls with call target: 55 % (threshold 85 %). Percentage of expressions with known type: 70 % (threshold 85 %)."}},
	      {"descriptor":{"id":"cs/diagnostics/successfully-extracted-files"},"message":{"text":""}}
	    ]}],
	    "results":[{"ruleId":"cs/security/test","message":{"text":"probe finding"}}]
	  }]
	}`)

	if err := ValidateCodeQLSARIF(sarif); err != nil {
		t.Fatalf("expected valid CodeQL SARIF to pass: %v", err)
	}
}

func TestValidateCodeQLSARIFAcceptsCodeQLCLIProofFixture(t *testing.T) {
	// Verifies parsing stays compatible with the SARIF shape emitted by the approved CodeQL 2.26.0 proof scan.
	sarifPath := filepath.Join("testdata", "codeql-cli-2.26.0-sarif.json")
	sarif, err := os.ReadFile(sarifPath)
	if err != nil {
		t.Fatalf("read CodeQL proof fixture: %v", err)
	}

	if err := ValidateCodeQLSARIF(sarif); err != nil {
		t.Fatalf("expected CodeQL proof fixture to pass: %v", err)
	}
}

func TestValidateCodeQLSARIFRejectsMissingRun(t *testing.T) {
	// Verifies a SARIF document without a CodeQL run cannot be mistaken for a successful zero-finding scan.
	sarif := []byte(`{"version":"2.1.0","runs":[]}`)

	if err := ValidateCodeQLSARIF(sarif); err == nil {
		t.Fatal("expected SARIF without a run to fail")
	}
}

func TestValidateCodeQLSARIFRejectsUnexpectedSchema(t *testing.T) {
	// Verifies an incompatible SARIF schema is rejected before an upload can report misleading results.
	sarif := validCodeQLSARIF(`"CodeQL"`, true, 55, 70)
	sarif = []byte(strings.Replace(string(sarif), "sarif-2.1.0.json", "sarif-2.0.0.json", 1))

	if err := ValidateCodeQLSARIF(sarif); err == nil {
		t.Fatal("expected unexpected SARIF schema to fail")
	}
}

func TestValidateCodeQLSARIFRejectsUnexpectedCodeQLVersion(t *testing.T) {
	// Verifies a CodeQL tool replacement is surfaced rather than silently changing the approved scanner.
	sarif := validCodeQLSARIF(`"CodeQL"`, true, 55, 70)
	sarif = []byte(strings.Replace(string(sarif), `"semanticVersion":"2.26.0"`, `"semanticVersion":"2.25.0"`, 1))

	if err := ValidateCodeQLSARIF(sarif); err == nil {
		t.Fatal("expected unexpected CodeQL version to fail")
	}
}

func TestValidateCodeQLSARIFFileRejectsMissingFile(t *testing.T) {
	// Verifies a missing scanner output file fails instead of being treated as an empty successful scan.
	missingPath := filepath.Join(t.TempDir(), "missing.sarif")

	if err := ValidateCodeQLSARIFFile(missingPath); err == nil {
		t.Fatal("expected missing SARIF file to fail")
	}
}

func TestValidateCodeQLSARIFRejectsUnexpectedTool(t *testing.T) {
	// Verifies results from another scanner cannot satisfy the CodeQL scan gate.
	sarif := validCodeQLSARIF(`"OtherScanner"`, true, 55, 70)

	if err := ValidateCodeQLSARIF(sarif); err == nil {
		t.Fatal("expected unexpected tool identity to fail")
	}
}

func TestValidateCodeQLSARIFRejectsUnsuccessfulInvocation(t *testing.T) {
	// Verifies a scanner failure is not accepted merely because it emitted SARIF.
	sarif := validCodeQLSARIF(`"CodeQL"`, false, 55, 70)

	if err := ValidateCodeQLSARIF(sarif); err == nil {
		t.Fatal("expected unsuccessful invocation to fail")
	}
}

func TestValidateCodeQLSARIFRejectsMissingBuildlessCompletion(t *testing.T) {
	// Verifies a SARIF report from an unexpected analysis mode cannot pass the no-build scan gate.
	sarif := validCodeQLSARIFWithoutCompletion()

	if err := ValidateCodeQLSARIF(sarif); err == nil {
		t.Fatal("expected missing build-mode completion diagnostic to fail")
	}
}

func TestValidateCodeQLSARIFRejectsQualityRegression(t *testing.T) {
	// Verifies reduced call-target or type-resolution quality fails before results are uploaded.
	for _, quality := range []struct {
		name        string
		callTargets int
		knownTypes  int
	}{
		{name: "call targets", callTargets: 54, knownTypes: 70},
		{name: "known types", callTargets: 55, knownTypes: 69},
	} {
		t.Run(quality.name, func(t *testing.T) {
			sarif := validCodeQLSARIF(`"CodeQL"`, true, quality.callTargets, quality.knownTypes)

			if err := ValidateCodeQLSARIF(sarif); err == nil {
				t.Fatal("expected quality regression to fail")
			}
		})
	}
}

func TestValidateCodeQLSARIFRejectsNoSuccessfullyExtractedFiles(t *testing.T) {
	// Verifies high percentage metrics cannot hide a database with no extracted C# source files.
	sarif := validCodeQLSARIFWithExtractedFiles(0)

	if err := ValidateCodeQLSARIF(sarif); err == nil {
		t.Fatal("expected empty extracted-file diagnostics to fail")
	}
}

func TestValidateCodeQLSARIFRejectsCompilationCollapse(t *testing.T) {
	// Verifies compilation diagnostics without extracted source cannot pass as a successful no-build analysis.
	sarif := []byte(`{
	  "$schema":"https://json.schemastore.org/sarif-2.1.0.json",
	  "version":"2.1.0",
	  "runs":[{
	    "tool":{"driver":{"name":"CodeQL","semanticVersion":"2.26.0"}},
	    "properties":{"metricResults":[{"ruleId":"cs/summary/lines-of-code","value":75000}]},
	    "invocations":[{"executionSuccessful":true,"toolExecutionNotifications":[
	      {"descriptor":{"id":"csharp/autobuilder/buildless/complete"},"message":{"text":"C# analysis with build-mode 'none' completed."}},
	      {"descriptor":{"id":"csharp/diagnostic/database-quality"},"message":{"text":"Some metrics of the database quality are: Percentage of calls with call target: 55 % (threshold 85 %). Percentage of expressions with known type: 70 % (threshold 85 %)."}},
	      {"descriptor":{"id":"cs/compilation-error"},"message":{"text":"Error CS0246 compilation failed"}}
	    ]}],
	    "results":[]
	  }]
	}`)

	if err := ValidateCodeQLSARIF(sarif); err == nil {
		t.Fatal("expected compilation collapse fixture to fail")
	}
}

func validCodeQLSARIF(toolName string, executionSuccessful bool, callTargets int, knownTypes int) []byte {
	return validCodeQLSARIFWithExtractedFilesAndValues(toolName, executionSuccessful, callTargets, knownTypes, 1)
}

func validCodeQLSARIFWithExtractedFiles(extractedFiles int) []byte {
	return validCodeQLSARIFWithExtractedFilesAndValues(`"CodeQL"`, true, 55, 70, extractedFiles)
}

func validCodeQLSARIFWithExtractedFilesAndValues(toolName string, executionSuccessful bool, callTargets int, knownTypes int, extractedFiles int) []byte {
	extractedFileNotifications := ""
	for index := 0; index < extractedFiles; index++ {
		extractedFileNotifications += `,{"descriptor":{"id":"cs/diagnostics/successfully-extracted-files"},"message":{"text":""}}`
	}
	return []byte(`{
	  "$schema":"https://json.schemastore.org/sarif-2.1.0.json",
	  "version":"2.1.0",
	  "runs":[{
	    "tool":{"driver":{"name":` + toolName + `,"semanticVersion":"2.26.0"}},
	    "properties":{"metricResults":[{"ruleId":"cs/summary/lines-of-code","value":75000}]},
	    "invocations":[{"executionSuccessful":` + boolJSON(executionSuccessful) + `,"toolExecutionNotifications":[
	      {"descriptor":{"id":"csharp/autobuilder/buildless/complete"},"message":{"text":"C# analysis with build-mode 'none' completed."}},
	      {"descriptor":{"id":"csharp/diagnostic/database-quality"},"message":{"text":"Some metrics of the database quality are: Percentage of calls with call target: ` + intJSON(callTargets) + ` % (threshold 85 %). Percentage of expressions with known type: ` + intJSON(knownTypes) + ` % (threshold 85 %)."}}` + extractedFileNotifications + `
	    ]}],
	    "results":[{"ruleId":"cs/security/test","message":{"text":"probe finding"}}]
	  }]
	}`)
}

func validCodeQLSARIFWithoutCompletion() []byte {
	return []byte(`{
	  "$schema":"https://json.schemastore.org/sarif-2.1.0.json",
	  "version":"2.1.0",
	  "runs":[{
	    "tool":{"driver":{"name":"CodeQL","semanticVersion":"2.26.0"}},
	    "properties":{"metricResults":[{"ruleId":"cs/summary/lines-of-code","value":75000}]},
	    "invocations":[{"executionSuccessful":true,"toolExecutionNotifications":[
	      {"descriptor":{"id":"csharp/diagnostic/database-quality"},"message":{"text":"Some metrics of the database quality are: Percentage of calls with call target: 55 % (threshold 85 %). Percentage of expressions with known type: 70 % (threshold 85 %)."}},
	      {"descriptor":{"id":"cs/diagnostics/successfully-extracted-files"},"message":{"text":""}}
	    ]}],
	    "results":[{"ruleId":"cs/security/test","message":{"text":"probe finding"}}]
	  }]
	}`)
}

func boolJSON(value bool) string {
	if value {
		return "true"
	}
	return "false"
}

func intJSON(value int) string {
	return string(rune('0'+value/10)) + string(rune('0'+value%10))
}
