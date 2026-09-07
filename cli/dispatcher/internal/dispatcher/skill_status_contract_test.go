package dispatcher

import (
	"encoding/json"
	"os"
	"path/filepath"
	"testing"

	"github.com/hatayama/unity-cli-loop/common/skillscan"
)

// The shared contract both this test and the Editor-side SkillStatusContractTests read, so
// neither side can loosen the comparable-skill-file rule without failing the other's CI.
const skillStatusContractPath = "tests/contracts/skill_status_contract.json"

type skillStatusContractFile struct {
	Comment string                    `json:"comment"`
	Cases   []skillStatusContractCase `json:"cases"`
}

type skillStatusContractCase struct {
	ID        string            `json:"id"`
	Source    map[string]string `json:"source"`
	Installed map[string]string `json:"installed"`
	Expected  string            `json:"expected"`
}

// Verifies target-mode skill status matches the shared skill status contract that the Editor also verifies.
func TestSkillStatusMatchesSharedContract(t *testing.T) {
	contract := readSkillStatusContract(t)
	for _, contractCase := range contract.Cases {
		t.Run(contractCase.ID, func(t *testing.T) {
			root := t.TempDir()
			sourceDir := filepath.Join(root, "source", "Skill")
			writeContractFiles(t, sourceDir, contractCase.Source)

			skill := skillDefinition{
				name:            "uloop-sample",
				content:         []byte(contractCase.Source[skillscan.SkillFileName]),
				sourceDirectory: sourceDir,
			}
			baseDir := filepath.Join(root, ".claude", "skills")
			writeContractFiles(t, getPreferredSkillDir(baseDir, skill.name, true), contractCase.Installed)

			status, err := getSkillStatus(baseDir, skill, true)
			if err != nil {
				t.Fatalf("case %s: getSkillStatus failed: %v", contractCase.ID, err)
			}
			if status != contractCase.Expected {
				t.Fatalf("case %s: expected %s, got %s", contractCase.ID, contractCase.Expected, status)
			}
		})
	}
}

func readSkillStatusContract(t *testing.T) skillStatusContractFile {
	t.Helper()

	data, err := os.ReadFile(findSkillStatusContractFile(t))
	if err != nil {
		t.Fatalf("failed to read shared skill status contract: %v", err)
	}

	var contract skillStatusContractFile
	if err := json.Unmarshal(data, &contract); err != nil {
		t.Fatalf("failed to unmarshal shared skill status contract: %v", err)
	}
	// An unreadable or empty contract would otherwise report as "every case passed".
	if len(contract.Cases) == 0 {
		t.Fatal("skill status contract must contain at least one case")
	}
	return contract
}

// findSkillStatusContractFile walks up from the working directory until the contract is found, so
// the test locates it regardless of which module directory `go test` runs in.
func findSkillStatusContractFile(t *testing.T) string {
	t.Helper()

	directory, err := os.Getwd()
	if err != nil {
		t.Fatalf("failed to resolve current directory: %v", err)
	}

	for {
		candidate := filepath.Join(directory, skillStatusContractPath)
		if _, err := os.Stat(candidate); err == nil {
			return candidate
		}

		parent := filepath.Dir(directory)
		if parent == directory {
			t.Fatalf("failed to find %s from %s", skillStatusContractPath, directory)
		}
		directory = parent
	}
}

// writeContractFiles materializes "relative path -> content" entries; paths use '/' in the
// contract and are converted with filepath.FromSlash so the test also runs on Windows.
func writeContractFiles(t *testing.T, root string, files map[string]string) {
	t.Helper()
	for relativePath, content := range files {
		fullPath := filepath.Join(root, filepath.FromSlash(relativePath))
		if err := os.MkdirAll(filepath.Dir(fullPath), 0o755); err != nil {
			t.Fatalf("failed to create directory for %s: %v", relativePath, err)
		}
		if err := os.WriteFile(fullPath, []byte(content), 0o644); err != nil {
			t.Fatalf("failed to write %s: %v", relativePath, err)
		}
	}
}
