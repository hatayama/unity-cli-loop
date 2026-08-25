// Package automation hosts the logic behind the release and CI commands. This file generates the
// description text in the embedded tool catalog from the package's own SKILL.md parameter tables, so
// the catalog is a build artifact of the skills rather than a third place to hand-maintain help text.
package automation

import (
	"encoding/json"
	"fmt"
	"io"
	"os"
	"path/filepath"
	"sort"
	"strings"

	"github.com/hatayama/unity-cli-loop/common/skilldocs"
	"github.com/hatayama/unity-cli-loop/common/tooldocs"
	"github.com/hatayama/unity-cli-loop/common/tools"
)

// CatalogRelativePath is the generated file, relative to the repository root. It is the only artifact
// this generator writes.
const CatalogRelativePath = "cli/common/tools/default-tools.json"

// SyncToolDocsConfig selects the repository to work on and whether to verify instead of write.
type SyncToolDocsConfig struct {
	RepositoryRoot string
	CheckOnly      bool
}

// toolsWithoutParameterTable are the tools allowed to have no parameter table. focus-window takes no
// parameters at all, so a table would have no rows to hold; its tool description still comes from its
// skill. The count is asserted against the catalog so a new tool cannot silently join this list.
//
// This is deliberately not DefaultToolsCatalogDriftTests' CliOwnedCommandsWithoutLiveUnityTools: that
// list names commands with no live Unity tool, which is a different question from having no
// parameters.
var toolsWithoutParameterTable = map[string]bool{
	"focus-window": true,
}

// cliOnlyParameterTableOptions records options parsed by the native runner before Unity sees a
// schema. Keeping this narrow lets the generator reject every other stale skill-table row.
var cliOnlyParameterTableOptions = map[string]map[string]bool{
	"run-tests": {
		tooldocs.RunTestsSkipCompileFlagName: true,
	},
}

// descriptionKey identifies one description in the catalog. Property is empty for the tool's own
// description.
type descriptionKey struct {
	Tool     string
	Property string
}

// RunSyncToolDocs regenerates the catalog's description text, or in check mode reports that it is out
// of date. Any mismatch between a schema and its skill table is an error rather than a silent skip:
// one of the two is stale, and only a human can say which.
func RunSyncToolDocs(stdout io.Writer, stderr io.Writer, config SyncToolDocsConfig) int {
	catalogPath := filepath.Join(config.RepositoryRoot, filepath.FromSlash(CatalogRelativePath))
	content, err := os.ReadFile(catalogPath)
	if err != nil {
		_, _ = fmt.Fprintf(stderr, "failed to read %s: %v\n", CatalogRelativePath, err)
		return 1
	}

	generated, err := GenerateCatalogWithSkillDescriptions(content, config.RepositoryRoot)
	if err != nil {
		_, _ = fmt.Fprintf(stderr, "%v\n", err)
		return 1
	}

	if config.CheckOnly {
		if string(generated) == string(content) {
			_, _ = fmt.Fprintf(stdout, "%s matches the skill parameter tables.\n", CatalogRelativePath)
			return 0
		}
		_, _ = fmt.Fprintf(stderr,
			"%s no longer matches the skill parameter tables. Run scripts/sync-tool-docs.sh and commit the result.\n",
			CatalogRelativePath)
		return 1
	}

	if string(generated) == string(content) {
		_, _ = fmt.Fprintf(stdout, "%s is already up to date.\n", CatalogRelativePath)
		return 0
	}
	if err := os.WriteFile(catalogPath, generated, 0o644); err != nil {
		_, _ = fmt.Fprintf(stderr, "failed to write %s: %v\n", CatalogRelativePath, err)
		return 1
	}
	_, _ = fmt.Fprintf(stdout, "Updated %s from the skill parameter tables.\n", CatalogRelativePath)
	return 0
}

// GenerateCatalogWithSkillDescriptions returns the catalog content with every description replaced by
// the text its skill states, leaving all other bytes untouched.
func GenerateCatalogWithSkillDescriptions(content []byte, repositoryRoot string) ([]byte, error) {
	catalog := tools.ToolCatalog{}
	if err := json.Unmarshal(content, &catalog); err != nil {
		return nil, fmt.Errorf("failed to parse %s: %w", CatalogRelativePath, err)
	}

	documented := skilldocs.Load(repositoryRoot)
	if len(documented) == 0 {
		return nil, fmt.Errorf("no skills were found under %s; is this the repository root?", repositoryRoot)
	}
	if err := verifyTablelessToolsCoverTheCatalog(catalog); err != nil {
		return nil, err
	}

	replacements := map[descriptionKey]string{}
	// Every mismatch is reported, not just the first: a table that fell behind the schema usually did
	// so for several tools at once, and fixing them one round trip at a time is what made the drift
	// accumulate in the first place.
	problems := []string{}
	for _, tool := range catalog.Tools {
		problems = append(problems, collectToolReplacements(tool, documented, replacements)...)
	}
	if len(problems) > 0 {
		return nil, fmt.Errorf("the skill parameter tables and the tool schemas disagree:\n  %s",
			strings.Join(problems, "\n  "))
	}
	return replaceCatalogDescriptions(content, replacements)
}

// verifyTablelessToolsCoverTheCatalog fails when the catalog grew a tool the allow-list does not
// account for. Without this the count silently drifts and a new tool's missing table looks
// intentional.
func verifyTablelessToolsCoverTheCatalog(catalog tools.ToolCatalog) error {
	for toolName := range toolsWithoutParameterTable {
		if _, ok := tools.Find(catalog, toolName); !ok {
			return fmt.Errorf("%q is allowed to have no parameter table but is not in the catalog", toolName)
		}
	}

	documentedWithTable := len(catalog.Tools) - len(toolsWithoutParameterTable)
	if documentedWithTable <= 0 {
		return fmt.Errorf("the catalog holds %d tools, which cannot all be table-less", len(catalog.Tools))
	}
	return nil
}

// collectToolReplacements records one tool's replacements and returns the mismatches found, so the
// caller can report every tool's drift in one run.
func collectToolReplacements(
	tool tools.ToolDefinition,
	documented map[string]skilldocs.ToolDocs,
	replacements map[descriptionKey]string,
) []string {
	docs, ok := documented[tool.Name]
	if !ok {
		return []string{fmt.Sprintf("%s has no skill; every tool's help text must come from a skill", tool.Name)}
	}
	if docs.ToolDescription == "" {
		return []string{fmt.Sprintf("%s has a skill with no tool description", tool.Name)}
	}
	replacements[descriptionKey{Tool: tool.Name}] = docs.ToolDescription

	problems := []string{}
	documentedOptions := map[string]bool{}
	for _, propertyName := range sortedPropertyNames(tool) {
		property := tool.EffectiveInputSchema().Properties[propertyName]
		optionName := tooldocs.OptionNameForProperty(tool.Name, propertyName, property)
		if property.Hidden {
			// A hidden property never reaches help, so requiring a documented row would force the
			// skill to describe something no caller can pass.
			continue
		}
		documentedOptions[optionName] = true

		description, ok := docs.ParamDescriptions[optionName]
		if !ok {
			problems = append(problems, fmt.Sprintf(
				"%s --%s is accepted by the tool but has no row in its skill parameter table",
				tool.Name, optionName))
			continue
		}
		replacements[descriptionKey{Tool: tool.Name, Property: propertyName}] = description
	}

	if len(documentedOptions) == 0 && !toolsWithoutParameterTable[tool.Name] {
		problems = append(problems, fmt.Sprintf(
			"%s accepts no visible parameters but is not listed as table-less", tool.Name))
	}
	return append(problems, unknownTableRowProblems(tool.Name, docs, documentedOptions)...)
}

// unknownTableRowProblems reports table rows matching no accepted option, which means the skill
// documents a flag the implementation dropped or renamed.
func unknownTableRowProblems(toolName string, docs skilldocs.ToolDocs, documentedOptions map[string]bool) []string {
	unknown := []string{}
	for optionName := range docs.ParamDescriptions {
		if cliOnlyParameterTableOptions[toolName][optionName] {
			continue
		}
		if !documentedOptions[optionName] {
			unknown = append(unknown, "--"+optionName)
		}
	}
	if len(unknown) == 0 {
		return nil
	}
	sort.Strings(unknown)
	return []string{fmt.Sprintf("%s documents %s in its skill parameter table, but the tool does not accept them",
		toolName, strings.Join(unknown, ", "))}
}

func sortedPropertyNames(tool tools.ToolDefinition) []string {
	names := make([]string, 0, len(tool.EffectiveInputSchema().Properties))
	for propertyName := range tool.EffectiveInputSchema().Properties {
		names = append(names, propertyName)
	}
	sort.Strings(names)
	return names
}
