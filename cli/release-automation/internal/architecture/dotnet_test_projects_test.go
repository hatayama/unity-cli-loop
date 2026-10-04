package architecture

import (
	"io/fs"
	"os"
	"path/filepath"
	"reflect"
	"sort"
	"strings"
	"testing"
)

// Tests that every dotnet test project under tests/ is run by a workflow triggered by pull_request.
func TestDotnetTestProjectsRunInPullRequestWorkflows(t *testing.T) {
	repositoryRoot := findRepositoryRoot(t)
	projects := dotnetTestProjectPaths(t, repositoryRoot)
	if len(projects) == 0 {
		t.Fatal("no dotnet test projects found under tests/; the scan is broken, so this guard would pass without checking anything")
	}
	workflows := [][]string{}
	for _, workflowPath := range workflowFilePaths(t, repositoryRoot) {
		workflows = append(workflows, readWorkflowLines(t, workflowPath))
	}
	missing := dotnetTestProjectsMissingFromPullRequestWorkflows(projects, workflows)
	if len(missing) > 0 {
		t.Fatalf("dotnet test projects that no pull request workflow runs:\n%s\nAdd `dotnet test <project> --configuration Release` to a workflow triggered by pull_request, as the \"Run concurrency unit tests\" step of .github/workflows/unity-compile-check-and-test-runner.yml does for the Unity-free unit test projects.", strings.Join(missing, "\n"))
	}
}

const pullRequestWorkflowHeader = "on:\n  pull_request:\njobs:\n  test:\n    runs-on: ubuntu-latest\n    steps:\n"

const scheduledWorkflowHeader = "on:\n  schedule:\n    - cron: '0 18 * * *'\n  workflow_dispatch:\njobs:\n  test:\n    runs-on: ubuntu-latest\n    steps:\n"

// Tests that only projects run with dotnet test by a pull request workflow count as wired.
func TestDotnetTestProjectsMissingFromPullRequestWorkflows(t *testing.T) {
	runBlockForA := "      - run: |\n          dotnet test tests/A/A.csproj --configuration Release\n"
	cases := []struct {
		name            string
		projects        []string
		workflow        string
		expectedMissing []string
	}{
		{
			name:            "run block in a pull request workflow",
			projects:        []string{"tests/A/A.csproj"},
			workflow:        pullRequestWorkflowHeader + runBlockForA,
			expectedMissing: []string{},
		},
		{
			name:            "single-line run",
			projects:        []string{"tests/A/A.csproj"},
			workflow:        pullRequestWorkflowHeader + "      - run: dotnet test tests/A/A.csproj\n",
			expectedMissing: []string{},
		},
		{
			name:            "scheduled workflow only",
			projects:        []string{"tests/A/A.csproj"},
			workflow:        scheduledWorkflowHeader + runBlockForA,
			expectedMissing: []string{"tests/A/A.csproj"},
		},
		{
			name:            "commented out",
			projects:        []string{"tests/A/A.csproj"},
			workflow:        pullRequestWorkflowHeader + "      - run: |\n          # dotnet test tests/A/A.csproj\n",
			expectedMissing: []string{"tests/A/A.csproj"},
		},
		{
			name:            "options first, quoted, dot-slash",
			projects:        []string{"tests/A/A.csproj"},
			workflow:        pullRequestWorkflowHeader + "      - run: dotnet test --configuration Release \"./tests/A/A.csproj\"\n",
			expectedMissing: []string{},
		},
		{
			name:            "backslash separators",
			projects:        []string{"tests/A/A.csproj"},
			workflow:        pullRequestWorkflowHeader + "      - run: dotnet test tests\\A\\A.csproj\n",
			expectedMissing: []string{},
		},
		{
			name:            "build is not test",
			projects:        []string{"tests/A/A.csproj"},
			workflow:        pullRequestWorkflowHeader + "      - run: dotnet build tests/A/A.csproj\n",
			expectedMissing: []string{"tests/A/A.csproj"},
		},
		{
			name:            "another project only",
			projects:        []string{"tests/A/A.csproj"},
			workflow:        pullRequestWorkflowHeader + "      - run: dotnet test tests/B/B.csproj\n",
			expectedMissing: []string{"tests/A/A.csproj"},
		},
		{
			name:            "chained command after the test",
			projects:        []string{"tests/A/A.csproj", "tests/B/B.csproj"},
			workflow:        pullRequestWorkflowHeader + "      - run: dotnet test tests/A/A.csproj && dotnet build tests/B/B.csproj\n",
			expectedMissing: []string{"tests/B/B.csproj"},
		},
		{
			name:            "every project referenced",
			projects:        []string{"tests/A/A.csproj", "tests/B/B.csproj"},
			workflow:        pullRequestWorkflowHeader + "      - run: |\n          dotnet test tests/A/A.csproj\n          dotnet test tests/B/B.csproj\n",
			expectedMissing: []string{},
		},
	}

	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			workflows := [][]string{strings.Split(testCase.workflow, "\n")}
			missing := dotnetTestProjectsMissingFromPullRequestWorkflows(testCase.projects, workflows)
			if !reflect.DeepEqual(missing, testCase.expectedMissing) {
				t.Fatalf("%s: expected missing %v, got %v", testCase.name, testCase.expectedMissing, missing)
			}
		})
	}
}

// Tests that the tests/ scan reports only csproj files referencing Microsoft.NET.Test.Sdk, with forward slashes.
func TestDotnetTestProjectPathsReportsOnlyTestProjects(t *testing.T) {
	repositoryRoot := t.TempDir()
	writeTestFile(t, filepath.Join(repositoryRoot, "tests", "Alpha", "Alpha.csproj"),
		"<Project>\n  <ItemGroup>\n    <PackageReference Include=\"Microsoft.NET.Test.Sdk\" Version=\"17.11.1\" />\n  </ItemGroup>\n</Project>\n")
	writeTestFile(t, filepath.Join(repositoryRoot, "tests", "Helper", "Helper.csproj"),
		"<Project>\n  <ItemGroup>\n    <PackageReference Include=\"Newtonsoft.Json\" Version=\"13.0.3\" />\n  </ItemGroup>\n</Project>\n")
	writeTestFile(t, filepath.Join(repositoryRoot, "tests", "Alpha", "readme.txt"), "Microsoft.NET.Test.Sdk\n")

	paths := dotnetTestProjectPaths(t, repositoryRoot)

	expected := []string{"tests/Alpha/Alpha.csproj"}
	if !reflect.DeepEqual(paths, expected) {
		t.Fatalf("expected %v, got %v", expected, paths)
	}
}

// dotnetTestProjectsMissingFromPullRequestWorkflows returns, in input order, the projects that no
// workflow triggered by pull_request runs with dotnet test.
func dotnetTestProjectsMissingFromPullRequestWorkflows(projects []string, workflows [][]string) []string {
	referenced := map[string]bool{}
	for _, lines := range workflows {
		if !workflowRunsOnPullRequest(lines) {
			continue
		}
		for _, project := range dotnetTestCommandProjects(lines) {
			referenced[project] = true
		}
	}
	missing := []string{}
	for _, project := range projects {
		if !referenced[project] {
			missing = append(missing, project)
		}
	}
	return missing
}

// dotnetTestCommandProjects returns the .csproj arguments of dotnet test commands in the workflow
// lines, with forward slashes and no leading "./". Commented-out text is ignored.
func dotnetTestCommandProjects(lines []string) []string {
	projects := []string{}
	for _, line := range lines {
		fields := strings.Fields(stripYamlComment(line))
		for index := 0; index+1 < len(fields); index++ {
			if fields[index] != "dotnet" || fields[index+1] != "test" {
				continue
			}
			projects = append(projects, csprojArguments(fields[index+2:])...)
		}
	}
	return projects
}

// csprojArguments returns the .csproj paths among the arguments of one command, stopping at the
// first shell operator so a chained command's arguments are not attributed to dotnet test.
func csprojArguments(arguments []string) []string {
	projects := []string{}
	for _, argument := range arguments {
		if argument == "&&" || argument == "||" || argument == "|" || argument == ";" {
			break
		}
		candidate := strings.Trim(argument, `"'`)
		candidate = strings.ReplaceAll(candidate, `\`, "/")
		candidate = strings.TrimPrefix(candidate, "./")
		if strings.HasSuffix(candidate, ".csproj") {
			projects = append(projects, candidate)
		}
	}
	return projects
}

// dotnetTestProjectPaths returns the repository-relative, forward-slash paths of every .csproj under
// tests/ that references Microsoft.NET.Test.Sdk, sorted.
func dotnetTestProjectPaths(t *testing.T, repositoryRoot string) []string {
	t.Helper()
	paths := []string{}
	walkErr := filepath.WalkDir(filepath.Join(repositoryRoot, "tests"), func(path string, entry fs.DirEntry, err error) error {
		if err != nil {
			return err
		}
		if entry.IsDir() || filepath.Ext(path) != ".csproj" {
			return nil
		}
		content, readErr := os.ReadFile(path)
		if readErr != nil {
			return readErr
		}
		if !strings.Contains(string(content), `Include="Microsoft.NET.Test.Sdk"`) {
			return nil
		}
		relativePath, relErr := filepath.Rel(repositoryRoot, path)
		if relErr != nil {
			return relErr
		}
		paths = append(paths, filepath.ToSlash(relativePath))
		return nil
	})
	if walkErr != nil {
		t.Fatalf("failed to scan tests/ for dotnet test projects: %v", walkErr)
	}
	sort.Strings(paths)
	return paths
}

func writeTestFile(t *testing.T, path string, content string) {
	t.Helper()
	if err := os.MkdirAll(filepath.Dir(path), 0o755); err != nil {
		t.Fatalf("failed to create directory for %s: %v", path, err)
	}
	if err := os.WriteFile(path, []byte(content), 0o644); err != nil {
		t.Fatalf("failed to write %s: %v", path, err)
	}
}
