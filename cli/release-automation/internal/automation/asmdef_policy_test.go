package automation

import (
	"bytes"
	"encoding/json"
	"os"
	"path/filepath"
	"strings"
	"testing"
)

type asmdefFixture struct {
	dir        string
	name       string
	guid       string
	references []string
}

func writeAsmdefFixture(t *testing.T, root string, fixture asmdefFixture) {
	t.Helper()
	writeAsmdefFixtureWithMeta(t, root, fixture, "fileFormatVersion: 2\nguid: "+fixture.guid+"\nAssemblyDefinitionImporter:\n")
}

func writeAsmdefFixtureWithMeta(t *testing.T, root string, fixture asmdefFixture, meta string) {
	t.Helper()
	directory := filepath.Join(root, filepath.FromSlash("Packages/src/"+fixture.dir))
	if err := os.MkdirAll(directory, 0o755); err != nil {
		t.Fatal(err)
	}
	document, err := json.Marshal(map[string]any{"name": fixture.name, "references": fixture.references})
	if err != nil {
		t.Fatal(err)
	}
	asmdefPath := filepath.Join(directory, fixture.name+".asmdef")
	if err := os.WriteFile(asmdefPath, document, 0o644); err != nil {
		t.Fatal(err)
	}
	if meta == "" {
		return
	}
	if err := os.WriteFile(asmdefPath+".meta", []byte(meta), 0o644); err != nil {
		t.Fatal(err)
	}
}

func writeAsmdefAllowlist(t *testing.T, path string, entries []asmdefAllowedReference) {
	t.Helper()
	content, err := json.Marshal(asmdefAllowlist{AllowedReferences: entries})
	if err != nil {
		t.Fatal(err)
	}
	if err := os.WriteFile(path, content, 0o644); err != nil {
		t.Fatal(err)
	}
}

func guidReference(guid string) string {
	return asmdefGUIDReferencePrefix + guid
}

const (
	fixtureGUIDDomain        = "11111111111111111111111111111111"
	fixtureGUIDApplication   = "22222222222222222222222222222222"
	fixtureGUIDToolA         = "33333333333333333333333333333333"
	fixtureGUIDToolB         = "44444444444444444444444444444444"
	fixtureGUIDToolContracts = "55555555555555555555555555555555"
)

func findAssembly(t *testing.T, assemblies []AsmdefAssembly, name string) AsmdefAssembly {
	t.Helper()
	for _, assembly := range assemblies {
		if assembly.Name == name {
			return assembly
		}
	}
	t.Fatalf("assembly %s not loaded: %v", name, assemblies)
	return AsmdefAssembly{}
}

// LoadAsmdefAssemblies resolves GUID references to assembly names through the
// sibling .asmdef.meta files.
func TestLoadAsmdefAssembliesResolvesGUIDReferences(t *testing.T) {
	root := t.TempDir()
	writeAsmdefFixture(t, root, asmdefFixture{dir: "Editor/Domain", name: asmdefLayerDomain, guid: fixtureGUIDDomain})
	writeAsmdefFixture(t, root, asmdefFixture{dir: "Editor/Application", name: asmdefLayerApplication, guid: fixtureGUIDApplication, references: []string{guidReference(fixtureGUIDDomain)}})

	assemblies, err := LoadAsmdefAssemblies(root)
	if err != nil {
		t.Fatal(err)
	}
	application := findAssembly(t, assemblies, asmdefLayerApplication)
	if len(application.References) != 1 || application.References[0] != asmdefLayerDomain {
		t.Fatalf("expected Application to reference Domain by name, got %v", application.References)
	}
	if application.Path != "Packages/src/Editor/Application/UnityCLILoop.Application.asmdef" {
		t.Fatalf("unexpected path: %s", application.Path)
	}
}

// LoadAsmdefAssemblies drops GUID references that match no loaded assembly:
// they point at external packages and are outside the policy.
func TestLoadAsmdefAssembliesIgnoresExternalGUIDReferences(t *testing.T) {
	root := t.TempDir()
	writeAsmdefFixture(t, root, asmdefFixture{dir: "Editor/Domain", name: asmdefLayerDomain, guid: fixtureGUIDDomain, references: []string{guidReference("99999999999999999999999999999999")}})

	assemblies, err := LoadAsmdefAssemblies(root)
	if err != nil {
		t.Fatal(err)
	}
	if len(findAssembly(t, assemblies, asmdefLayerDomain).References) != 0 {
		t.Fatalf("expected the external GUID reference to be dropped, got %v", assemblies)
	}
}

// LoadAsmdefAssemblies keeps plain-name references that match a loaded
// assembly and drops plain names of external assemblies such as the Unity
// test runner, so those never surface as violations.
func TestLoadAsmdefAssembliesHandlesPlainNameReferences(t *testing.T) {
	root := t.TempDir()
	writeAsmdefFixture(t, root, asmdefFixture{dir: "Editor/Domain", name: asmdefLayerDomain, guid: fixtureGUIDDomain})
	writeAsmdefFixture(t, root, asmdefFixture{dir: "Editor/Application", name: asmdefLayerApplication, guid: fixtureGUIDApplication, references: []string{asmdefLayerDomain, "UnityEditor.TestRunner"}})

	assemblies, err := LoadAsmdefAssemblies(root)
	if err != nil {
		t.Fatal(err)
	}
	references := findAssembly(t, assemblies, asmdefLayerApplication).References
	if len(references) != 1 || references[0] != asmdefLayerDomain {
		t.Fatalf("expected only the internal plain-name reference, got %v", references)
	}
}

// LoadAsmdefAssemblies reads the guid from a .meta file with CRLF line endings,
// which Windows checkouts produce.
func TestLoadAsmdefAssembliesReadsCRLFMeta(t *testing.T) {
	root := t.TempDir()
	writeAsmdefFixtureWithMeta(t, root, asmdefFixture{dir: "Editor/Domain", name: asmdefLayerDomain, guid: fixtureGUIDDomain}, "fileFormatVersion: 2\r\nguid: "+fixtureGUIDDomain+"\r\nAssemblyDefinitionImporter:\r\n")
	writeAsmdefFixture(t, root, asmdefFixture{dir: "Editor/Application", name: asmdefLayerApplication, guid: fixtureGUIDApplication, references: []string{guidReference(fixtureGUIDDomain)}})

	assemblies, err := LoadAsmdefAssemblies(root)
	if err != nil {
		t.Fatal(err)
	}
	if references := findAssembly(t, assemblies, asmdefLayerApplication).References; len(references) != 1 {
		t.Fatalf("expected the CRLF meta guid to resolve, got %v", references)
	}
}

// LoadAsmdefAssemblies fails when an .asmdef has no .meta file, because the GUID
// needed to resolve references to it is missing.
func TestLoadAsmdefAssembliesFailsWithoutMeta(t *testing.T) {
	root := t.TempDir()
	writeAsmdefFixtureWithMeta(t, root, asmdefFixture{dir: "Editor/Domain", name: asmdefLayerDomain, guid: fixtureGUIDDomain}, "")

	_, err := LoadAsmdefAssemblies(root)
	if err == nil || !strings.Contains(err.Error(), ".meta") {
		t.Fatalf("expected a missing-meta error, got %v", err)
	}
}

// LoadAsmdefAssemblies fails when no .asmdef exists under the root, so running
// from the wrong directory cannot pass as a silent no-op.
func TestLoadAsmdefAssembliesFailsWhenNothingFound(t *testing.T) {
	_, err := LoadAsmdefAssemblies(t.TempDir())
	if err == nil || !strings.Contains(err.Error(), "no .asmdef files found") {
		t.Fatalf("expected an empty-scan error, got %v", err)
	}
}

// LoadAsmdefAssemblies skips tilde-suffixed directories, which Unity ignores.
func TestLoadAsmdefAssembliesSkipsTildeDirectories(t *testing.T) {
	root := t.TempDir()
	writeAsmdefFixture(t, root, asmdefFixture{dir: "Editor/Domain", name: asmdefLayerDomain, guid: fixtureGUIDDomain})
	writeAsmdefFixtureWithMeta(t, root, asmdefFixture{dir: "Editor/Ignored~", name: "UnityCLILoop.Mystery", guid: fixtureGUIDToolA}, "")

	assemblies, err := LoadAsmdefAssemblies(root)
	if err != nil {
		t.Fatal(err)
	}
	if len(assemblies) != 1 {
		t.Fatalf("expected only the Domain assembly, got %v", assemblies)
	}
}

func toolName(tool string) string {
	return asmdefToolPrefix + tool + asmdefToolSuffix
}

func commonName(utility string) string {
	return asmdefToolCommonPrefix + utility + asmdefToolSuffix
}

// evaluateAsmdefPolicy reports each forbidden reference with the rule it
// violates. The table covers the current violations in the repository and the
// rules that currently have no violating reference, so a checker that
// accidentally allows everything still fails here.
func TestEvaluateAsmdefPolicyReportsForbiddenReferences(t *testing.T) {
	cases := []struct {
		name string
		from string
		to   string
		rule string
	}{
		{name: "tool to tool", from: toolName("A"), to: toolName("B"), rule: asmdefRuleToolIsolation},
		{name: "tool to infrastructure", from: toolName("A"), to: asmdefLayerInfrastructure, rule: asmdefRuleLayerDirection},
		{name: "tool to composition root", from: toolName("A"), to: asmdefLayerCompositionRoot, rule: asmdefRuleLayerDirection},
		{name: "common to application", from: commonName("X"), to: asmdefLayerApplication, rule: asmdefRuleCommonLayering},
		{name: "common to tool", from: commonName("X"), to: toolName("A"), rule: asmdefRuleCommonLayering},
		{name: "domain to application", from: asmdefLayerDomain, to: asmdefLayerApplication, rule: asmdefRuleLayerDirection},
		{name: "tool contracts to domain", from: asmdefLayerToolContracts, to: asmdefLayerDomain, rule: asmdefRuleLayerDirection},
		{name: "presentation to infrastructure", from: asmdefLayerPresentation, to: asmdefLayerInfrastructure, rule: asmdefRuleLayerDirection},
		{name: "runtime to layer", from: "UnityCLILoop.Runtime", to: asmdefLayerDomain, rule: asmdefRuleRuntimeIsolation},
		{name: "tool-owned runtime to application", from: asmdefToolPrefix + "A.Runtime", to: asmdefLayerApplication, rule: asmdefRuleRuntimeIsolation},
		{name: "internal bridge to layer", from: asmdefInternalBridgeName, to: asmdefLayerDomain, rule: asmdefRuleLayerDirection},
		{name: "umbrella to infrastructure", from: asmdefToolsUmbrellaName, to: asmdefLayerInfrastructure, rule: asmdefRuleUmbrellaScope},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			findings, err := evaluateAsmdefPolicy([]AsmdefAssembly{
				{Name: testCase.from, Path: "from.asmdef", References: []string{testCase.to}},
				{Name: testCase.to, Path: "to.asmdef"},
			})
			if err != nil {
				t.Fatal(err)
			}
			if len(findings) != 1 {
				t.Fatalf("expected one finding, got %v", findings)
			}
			if findings[0].Rule != testCase.rule || findings[0].From != testCase.from || findings[0].To != testCase.to {
				t.Fatalf("unexpected finding %+v", findings[0])
			}
		})
	}
}

// evaluateAsmdefPolicy accepts one representative reference for every allowed
// row of the policy table, so a rule that accidentally forbids a legitimate
// direction is caught as well.
func TestEvaluateAsmdefPolicyAcceptsAllowedReferences(t *testing.T) {
	cases := []struct {
		name string
		from string
		to   string
	}{
		{name: "domain to contracts", from: asmdefLayerDomain, to: asmdefLayerToolContracts},
		{name: "application to domain", from: asmdefLayerApplication, to: asmdefLayerDomain},
		{name: "infrastructure to application", from: asmdefLayerInfrastructure, to: asmdefLayerApplication},
		{name: "infrastructure to internal bridge", from: asmdefLayerInfrastructure, to: asmdefInternalBridgeName},
		{name: "infrastructure to runtime", from: asmdefLayerInfrastructure, to: "UnityCLILoop.PausePoints.Runtime"},
		{name: "infrastructure to common", from: asmdefLayerInfrastructure, to: commonName("Console")},
		{name: "presentation to application", from: asmdefLayerPresentation, to: asmdefLayerApplication},
		{name: "composition root to infrastructure", from: asmdefLayerCompositionRoot, to: asmdefLayerInfrastructure},
		{name: "composition root to umbrella", from: asmdefLayerCompositionRoot, to: asmdefToolsUmbrellaName},
		{name: "runtime to runtime", from: "UnityCLILoop.PausePoints.Runtime", to: "UnityCLILoop.Runtime"},
		{name: "umbrella to tool", from: asmdefToolsUmbrellaName, to: toolName("A")},
		{name: "umbrella to common", from: asmdefToolsUmbrellaName, to: commonName("X")},
		{name: "common to contracts", from: commonName("X"), to: asmdefLayerToolContracts},
		{name: "common to common", from: commonName("X"), to: commonName("Y")},
		{name: "common to runtime", from: commonName("X"), to: "UnityCLILoop.Runtime"},
		{name: "tool to contracts", from: toolName("A"), to: asmdefLayerToolContracts},
		{name: "tool to domain", from: toolName("A"), to: asmdefLayerDomain},
		{name: "tool to application", from: toolName("A"), to: asmdefLayerApplication},
		{name: "tool to common", from: toolName("A"), to: commonName("X")},
		{name: "tool to runtime", from: toolName("A"), to: "UnityCLILoop.PausePoints.Runtime"},
		{name: "tool to its own runtime assembly", from: toolName("A"), to: asmdefToolPrefix + "A.Runtime"},
		{name: "tool to internal bridge", from: toolName("A"), to: asmdefInternalBridgeName},
		{name: "sub-assembly to parent tool", from: toolName("RunTests.TestFramework"), to: toolName("RunTests")},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			findings, err := evaluateAsmdefPolicy([]AsmdefAssembly{
				{Name: testCase.from, Path: "from.asmdef", References: []string{testCase.to}},
				{Name: testCase.to, Path: "to.asmdef"},
			})
			if err != nil {
				t.Fatal(err)
			}
			if len(findings) != 0 {
				t.Fatalf("expected no findings, got %v", findings)
			}
		})
	}
}

// evaluateAsmdefPolicy fails on an assembly whose name matches no category,
// so a new assembly must follow the naming convention before it is accepted.
// A FirstPartyTools assembly without the .Editor or .Runtime suffix is one
// such name.
func TestEvaluateAsmdefPolicyRejectsUnknownAssembly(t *testing.T) {
	for _, name := range []string{"UnityCLILoop.Mystery", asmdefToolPrefix + "A"} {
		_, err := evaluateAsmdefPolicy([]AsmdefAssembly{{Name: name, Path: "mystery.asmdef"}})
		if err == nil || !strings.Contains(err.Error(), name) {
			t.Fatalf("expected an unknown-category error for %s, got %v", name, err)
		}
	}
}

// applyAsmdefAllowlist drops findings that match an entry and reports entries
// that matched nothing as stale.
func TestApplyAsmdefAllowlistSeparatesMatchedAndStaleEntries(t *testing.T) {
	findings := []AsmdefPolicyFinding{
		{From: toolName("A"), To: toolName("B"), Rule: asmdefRuleToolIsolation},
		{From: toolName("A"), To: toolName("C"), Rule: asmdefRuleToolIsolation},
	}
	allowlist := asmdefAllowlist{AllowedReferences: []asmdefAllowedReference{
		{From: toolName("A"), To: toolName("B"), Reason: "kept"},
		{From: asmdefLayerDomain, To: asmdefLayerApplication, Reason: "repaid"},
	}}

	remaining, stale := applyAsmdefAllowlist(findings, allowlist)
	if len(remaining) != 1 || remaining[0].To != toolName("C") {
		t.Fatalf("expected only the unlisted finding to remain, got %v", remaining)
	}
	if len(stale) != 1 || stale[0].From != asmdefLayerDomain {
		t.Fatalf("expected the repaid entry to be stale, got %v", stale)
	}
}

func writePolicyFixtureRepository(t *testing.T, root string, toolAReferences []string) {
	t.Helper()
	writeAsmdefFixture(t, root, asmdefFixture{dir: "Editor/ToolContracts", name: asmdefLayerToolContracts, guid: fixtureGUIDToolContracts})
	writeAsmdefFixture(t, root, asmdefFixture{dir: "Editor/FirstPartyTools/A", name: toolName("A"), guid: fixtureGUIDToolA, references: toolAReferences})
	writeAsmdefFixture(t, root, asmdefFixture{dir: "Editor/FirstPartyTools/B", name: toolName("B"), guid: fixtureGUIDToolB, references: []string{guidReference(fixtureGUIDToolContracts)}})
}

// RunAsmdefPolicyCheck exits 0 when every reference is allowed and the
// allowlist is empty.
func TestRunAsmdefPolicyCheckPassesCompliantRepository(t *testing.T) {
	root := t.TempDir()
	writePolicyFixtureRepository(t, root, []string{guidReference(fixtureGUIDToolContracts)})
	allowlistPath := filepath.Join(root, "allowlist.json")
	writeAsmdefAllowlist(t, allowlistPath, []asmdefAllowedReference{})

	stdout := bytes.Buffer{}
	stderr := bytes.Buffer{}
	exitCode := RunAsmdefPolicyCheck(&stdout, &stderr, AsmdefPolicyCheckOptions{Root: root, AllowlistPath: allowlistPath})
	if exitCode != 0 {
		t.Fatalf("expected exit 0, got %d (stdout %q, stderr %q)", exitCode, stdout.String(), stderr.String())
	}
	if !strings.Contains(stdout.String(), "No asmdef reference violated the policy.") {
		t.Fatalf("stdout did not report success: %q", stdout.String())
	}
}

// RunAsmdefPolicyCheck exits 1 and names the reference and rule when a tool
// references another tool that is not allowlisted.
func TestRunAsmdefPolicyCheckFailsOnUnlistedViolation(t *testing.T) {
	root := t.TempDir()
	writePolicyFixtureRepository(t, root, []string{guidReference(fixtureGUIDToolB)})
	allowlistPath := filepath.Join(root, "allowlist.json")
	writeAsmdefAllowlist(t, allowlistPath, []asmdefAllowedReference{})

	stdout := bytes.Buffer{}
	stderr := bytes.Buffer{}
	exitCode := RunAsmdefPolicyCheck(&stdout, &stderr, AsmdefPolicyCheckOptions{Root: root, AllowlistPath: allowlistPath})
	if exitCode != 1 {
		t.Fatalf("expected exit 1, got %d", exitCode)
	}
	expected := toolName("A") + " -> " + toolName("B") + ": " + asmdefRuleToolIsolation + " (Packages/src/Editor/FirstPartyTools/A/" + toolName("A") + ".asmdef)"
	if !strings.Contains(stdout.String(), expected) {
		t.Fatalf("stdout did not name the violation %q: %q", expected, stdout.String())
	}
}

// RunAsmdefPolicyCheck exits 0 when the only violation is allowlisted, and
// exits 1 with a stale message once that reference has been removed but the
// allowlist entry remains.
func TestRunAsmdefPolicyCheckHonoursAndExpiresAllowlist(t *testing.T) {
	root := t.TempDir()
	writePolicyFixtureRepository(t, root, []string{guidReference(fixtureGUIDToolB)})
	allowlistPath := filepath.Join(root, "allowlist.json")
	writeAsmdefAllowlist(t, allowlistPath, []asmdefAllowedReference{{From: toolName("A"), To: toolName("B"), Reason: "pending extraction"}})

	stdout := bytes.Buffer{}
	stderr := bytes.Buffer{}
	if exitCode := RunAsmdefPolicyCheck(&stdout, &stderr, AsmdefPolicyCheckOptions{Root: root, AllowlistPath: allowlistPath}); exitCode != 0 {
		t.Fatalf("expected the allowlisted violation to pass, got exit %d: %q", exitCode, stdout.String())
	}

	// Repay the debt without touching the allowlist: the entry is now stale.
	writeAsmdefFixture(t, root, asmdefFixture{dir: "Editor/FirstPartyTools/A", name: toolName("A"), guid: fixtureGUIDToolA, references: []string{guidReference(fixtureGUIDToolContracts)}})
	stdout.Reset()
	exitCode := RunAsmdefPolicyCheck(&stdout, &stderr, AsmdefPolicyCheckOptions{Root: root, AllowlistPath: allowlistPath})
	if exitCode != 1 {
		t.Fatalf("expected the stale allowlist entry to fail, got exit %d", exitCode)
	}
	if !strings.Contains(stdout.String(), "stale allowlist entry: "+toolName("A")+" -> "+toolName("B")) {
		t.Fatalf("stdout did not report the stale entry: %q", stdout.String())
	}
}

// RunAsmdefPolicyCheck exits 1 when the allowlist file is missing or an entry
// lacks a reason, so exemptions cannot be added silently.
func TestRunAsmdefPolicyCheckRejectsMalformedAllowlist(t *testing.T) {
	root := t.TempDir()
	writePolicyFixtureRepository(t, root, []string{guidReference(fixtureGUIDToolContracts)})
	allowlistPath := filepath.Join(root, "allowlist.json")
	writeAsmdefAllowlist(t, allowlistPath, []asmdefAllowedReference{{From: toolName("A"), To: toolName("B")}})

	stdout := bytes.Buffer{}
	stderr := bytes.Buffer{}
	if exitCode := RunAsmdefPolicyCheck(&stdout, &stderr, AsmdefPolicyCheckOptions{Root: root, AllowlistPath: allowlistPath}); exitCode != 1 {
		t.Fatalf("expected exit 1 for an entry without a reason, got %d", exitCode)
	}
	if !strings.Contains(stderr.String(), "reason") {
		t.Fatalf("stderr did not explain the malformed entry: %q", stderr.String())
	}
}
