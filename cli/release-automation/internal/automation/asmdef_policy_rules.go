package automation

import (
	"fmt"
	"sort"
	"strings"
)

// asmdefCategory is the role an assembly plays in the package architecture.
// It is derived from the assembly name alone so that a new assembly following
// the naming convention is classified without touching the checker.
type asmdefCategory string

const (
	asmdefCategoryLayer          asmdefCategory = "Layer"
	asmdefCategoryInternalBridge asmdefCategory = "InternalBridge"
	asmdefCategoryRuntime        asmdefCategory = "Runtime"
	asmdefCategoryToolsUmbrella  asmdefCategory = "ToolsUmbrella"
	asmdefCategoryToolCommon     asmdefCategory = "ToolCommon"
	asmdefCategoryTool           asmdefCategory = "Tool"
	asmdefCategoryUnknown        asmdefCategory = "Unknown"
)

const (
	asmdefLayerDomain          = "UnityCLILoop.Domain"
	asmdefLayerApplication     = "UnityCLILoop.Application"
	asmdefLayerInfrastructure  = "UnityCLILoop.Infrastructure"
	asmdefLayerPresentation    = "UnityCLILoop.Presentation"
	asmdefLayerCompositionRoot = "UnityCLILoop.CompositionRoot.Editor"
	asmdefLayerToolContracts   = "UnityCLILoop.ToolContracts"

	asmdefInternalBridgeName = "Unity.InternalAPIEditorBridge.024"
	asmdefToolsUmbrellaName  = "UnityCLILoop.FirstPartyTools.Editor"
	asmdefToolPrefix         = "UnityCLILoop.FirstPartyTools."
	asmdefToolCommonPrefix   = "UnityCLILoop.FirstPartyTools.Common."
	asmdefToolSuffix         = ".Editor"
	asmdefRuntimeSuffix      = ".Runtime"
)

// Rule identifiers reported in findings. Each names the architectural
// boundary that the reference crosses.
const (
	asmdefRuleLayerDirection   = "layer-direction"
	asmdefRuleRuntimeIsolation = "runtime-isolation"
	asmdefRuleUmbrellaScope    = "umbrella-scope"
	asmdefRuleCommonLayering   = "common-layering"
	asmdefRuleToolIsolation    = "tool-isolation"
)

// asmdefLayerAllowedTargets lists, per layer assembly, the layer assemblies it
// may reference. Non-layer categories a layer may reference are handled in
// asmdefLayerAllowedCategories.
var asmdefLayerAllowedTargets = map[string][]string{
	asmdefLayerToolContracts:   {},
	asmdefLayerDomain:          {asmdefLayerToolContracts},
	asmdefLayerApplication:     {asmdefLayerDomain, asmdefLayerToolContracts},
	asmdefLayerInfrastructure:  {asmdefLayerApplication, asmdefLayerDomain, asmdefLayerToolContracts},
	asmdefLayerPresentation:    {asmdefLayerApplication, asmdefLayerDomain, asmdefLayerToolContracts},
	asmdefLayerCompositionRoot: {asmdefLayerApplication, asmdefLayerDomain, asmdefLayerInfrastructure, asmdefLayerPresentation, asmdefLayerToolContracts},
}

// asmdefLayerAllowedCategories lists, per layer assembly, the non-layer
// categories it may reference. Infrastructure may use shared tool utilities
// (Console log access lives there) and the Unity internal bridge; the
// composition root wires everything and may see the tools umbrella.
var asmdefLayerAllowedCategories = map[string][]asmdefCategory{
	asmdefLayerInfrastructure:  {asmdefCategoryInternalBridge, asmdefCategoryRuntime, asmdefCategoryToolCommon},
	asmdefLayerCompositionRoot: {asmdefCategoryToolsUmbrella, asmdefCategoryRuntime, asmdefCategoryInternalBridge},
}

// classifyAsmdef derives the category from the assembly name. Runtime is
// tested before the FirstPartyTools prefixes so that a tool-owned runtime
// assembly (FirstPartyTools.<Tool>.Runtime) is held to the Runtime rules rather
// than being granted a tool's wider permissions.
func classifyAsmdef(name string) asmdefCategory {
	switch {
	case name == asmdefInternalBridgeName:
		return asmdefCategoryInternalBridge
	case name == asmdefToolsUmbrellaName:
		return asmdefCategoryToolsUmbrella
	case strings.HasSuffix(name, asmdefRuntimeSuffix):
		return asmdefCategoryRuntime
	case strings.HasPrefix(name, asmdefToolCommonPrefix) && strings.HasSuffix(name, asmdefToolSuffix):
		return asmdefCategoryToolCommon
	case strings.HasPrefix(name, asmdefToolPrefix) && strings.HasSuffix(name, asmdefToolSuffix):
		return asmdefCategoryTool
	}
	if _, isLayer := asmdefLayerAllowedTargets[name]; isLayer {
		return asmdefCategoryLayer
	}
	return asmdefCategoryUnknown
}

// evaluateAsmdefPolicy returns every reference the policy forbids, sorted by
// (from, to). An assembly whose name matches no category is an error rather
// than a finding: the naming convention must be extended before the checker
// can say anything meaningful about it.
func evaluateAsmdefPolicy(assemblies []AsmdefAssembly) ([]AsmdefPolicyFinding, error) {
	findings := []AsmdefPolicyFinding{}
	for _, assembly := range assemblies {
		if classifyAsmdef(assembly.Name) == asmdefCategoryUnknown {
			return nil, fmt.Errorf("%s (%s) matches no assembly category; follow the naming convention in docs/asmdef-policy.md", assembly.Name, assembly.Path)
		}
		for _, target := range assembly.References {
			rule := asmdefReferenceViolation(assembly.Name, target)
			if rule == "" {
				continue
			}
			findings = append(findings, AsmdefPolicyFinding{
				From: assembly.Name,
				To:   target,
				Rule: rule,
				Path: assembly.Path,
			})
		}
	}
	sort.Slice(findings, func(left int, right int) bool {
		if findings[left].From != findings[right].From {
			return findings[left].From < findings[right].From
		}
		return findings[left].To < findings[right].To
	})
	return findings, nil
}

// asmdefCategoryPermit lists what one non-layer category may reference: named
// layer assemblies and whole categories.
type asmdefCategoryPermit struct {
	targets    []string
	categories []asmdefCategory
}

// asmdefCategoryPermits is the permit table for the non-layer categories.
// Layers are handled by asmdefLayerAllowedTargets / asmdefLayerAllowedCategories
// because their permissions differ per assembly, not per category.
var asmdefCategoryPermits = map[asmdefCategory]asmdefCategoryPermit{
	asmdefCategoryInternalBridge: {},
	asmdefCategoryRuntime:        {categories: []asmdefCategory{asmdefCategoryRuntime}},
	asmdefCategoryToolsUmbrella: {
		targets:    []string{asmdefLayerToolContracts, asmdefLayerDomain},
		categories: []asmdefCategory{asmdefCategoryTool, asmdefCategoryToolCommon},
	},
	asmdefCategoryToolCommon: {
		targets:    []string{asmdefLayerToolContracts, asmdefLayerDomain},
		categories: []asmdefCategory{asmdefCategoryToolCommon, asmdefCategoryRuntime, asmdefCategoryInternalBridge},
	},
	asmdefCategoryTool: {
		targets:    []string{asmdefLayerToolContracts, asmdefLayerDomain, asmdefLayerApplication},
		categories: []asmdefCategory{asmdefCategoryToolCommon, asmdefCategoryRuntime, asmdefCategoryInternalBridge},
	},
}

// asmdefCategoryRules names the rule a non-layer category violates when it
// references something outside its permit. Tool is absent because its rule
// depends on the target (see asmdefToolReferenceViolation).
var asmdefCategoryRules = map[asmdefCategory]string{
	asmdefCategoryInternalBridge: asmdefRuleLayerDirection,
	asmdefCategoryRuntime:        asmdefRuleRuntimeIsolation,
	asmdefCategoryToolsUmbrella:  asmdefRuleUmbrellaScope,
	asmdefCategoryToolCommon:     asmdefRuleCommonLayering,
}

// asmdefReferenceViolation returns the violated rule identifier, or "" when the
// reference is allowed.
func asmdefReferenceViolation(from string, to string) string {
	fromCategory := classifyAsmdef(from)
	toCategory := classifyAsmdef(to)
	if fromCategory == asmdefCategoryLayer {
		if asmdefLayerMayReference(from, to, toCategory) {
			return ""
		}
		return asmdefRuleLayerDirection
	}
	if asmdefPermitAllows(asmdefCategoryPermits[fromCategory], to, toCategory) {
		return ""
	}
	if fromCategory == asmdefCategoryTool {
		return asmdefToolReferenceViolation(from, to, toCategory)
	}
	return asmdefCategoryRules[fromCategory]
}

func asmdefPermitAllows(permit asmdefCategoryPermit, to string, toCategory asmdefCategory) bool {
	for _, allowed := range permit.targets {
		if allowed == to {
			return true
		}
	}
	for _, allowed := range permit.categories {
		if allowed == toCategory {
			return true
		}
	}
	return false
}

func asmdefLayerMayReference(from string, to string, toCategory asmdefCategory) bool {
	return asmdefPermitAllows(asmdefCategoryPermit{
		targets:    asmdefLayerAllowedTargets[from],
		categories: asmdefLayerAllowedCategories[from],
	}, to, toCategory)
}

// asmdefToolReferenceViolation names the rule for a tool reference outside the
// Tool permit: another tool is a tool-isolation violation unless it is the
// tool's own parent; any other target is a layer-direction violation.
func asmdefToolReferenceViolation(from string, to string, toCategory asmdefCategory) string {
	if toCategory != asmdefCategoryTool {
		return asmdefRuleLayerDirection
	}
	if asmdefIsParentTool(from, to) {
		return ""
	}
	return asmdefRuleToolIsolation
}

// asmdefIsParentTool reports whether to is an ancestor tool of from, e.g.
// RunTests is the parent of RunTests.TestFramework. Sub-assemblies of a tool
// may reference the tool they belong to.
func asmdefIsParentTool(from string, to string) bool {
	fromTool := asmdefToolName(from)
	toTool := asmdefToolName(to)
	return strings.HasPrefix(fromTool, toTool+".")
}

func asmdefToolName(name string) string {
	return strings.TrimSuffix(strings.TrimPrefix(name, asmdefToolPrefix), asmdefToolSuffix)
}

// applyAsmdefAllowlist removes allowlisted findings and returns the allowlist
// entries that matched nothing, which means the reference was repaid and the
// entry must be deleted.
func applyAsmdefAllowlist(findings []AsmdefPolicyFinding, allowlist asmdefAllowlist) ([]AsmdefPolicyFinding, []asmdefAllowedReference) {
	matched := make([]bool, len(allowlist.AllowedReferences))
	remaining := []AsmdefPolicyFinding{}
	for _, finding := range findings {
		allowed := false
		for index, entry := range allowlist.AllowedReferences {
			if entry.From == finding.From && entry.To == finding.To {
				matched[index] = true
				allowed = true
			}
		}
		if !allowed {
			remaining = append(remaining, finding)
		}
	}
	stale := []asmdefAllowedReference{}
	for index, entry := range allowlist.AllowedReferences {
		if !matched[index] {
			stale = append(stale, entry)
		}
	}
	return remaining, stale
}
