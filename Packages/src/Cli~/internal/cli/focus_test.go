package cli

import (
	"strings"
	"testing"
)

func TestParseMacUnityProcessesExtractsProjectPath(t *testing.T) {
	output := `123 /Applications/Unity/Hub/Editor/6000.0.0f1/Unity.app/Contents/MacOS/Unity -projectPath "/Users/ExampleUser/My Project" -useHub -hubIPC
456 /Applications/Unity/Hub/Editor/6000.0.0f1/Unity.app/Contents/MacOS/Unity -batchmode -projectPath "/Users/ExampleUser/Batch"
789 /Applications/Unity/Hub/Editor/6000.0.0f1/Unity.app/Contents/MacOS/Unity -projectPath /Users/ExampleUser/Other -logFile -
`

	processes := parseMacUnityProcesses(output)

	if len(processes) != 2 {
		t.Fatalf("process count mismatch: %#v", processes)
	}
	if processes[0].pid != 123 || processes[0].projectPath != "/Users/ExampleUser/My Project" {
		t.Fatalf("first process mismatch: %#v", processes[0])
	}
	if processes[1].pid != 789 || processes[1].projectPath != "/Users/ExampleUser/Other" {
		t.Fatalf("second process mismatch: %#v", processes[1])
	}
}

func TestParseWindowsUnityProcessesExtractsProjectPath(t *testing.T) {
	output := `123|C:\Program Files\Unity\Hub\Editor\6000.0.0f1\Editor\Unity.exe -projectPath "C:\Users\ExampleUser\My Project" -useHub
456|C:\Program Files\Unity\Hub\Editor\6000.0.0f1\Editor\Unity.exe -batchmode -projectPath "C:\Users\ExampleUser\Batch"
`

	processes := parseWindowsUnityProcesses(output)

	if len(processes) != 1 {
		t.Fatalf("process count mismatch: %#v", processes)
	}
	if processes[0].pid != 123 || processes[0].projectPath != `C:\Users\ExampleUser\My Project` {
		t.Fatalf("process mismatch: %#v", processes[0])
	}
}

func TestExtractProjectPathSupportsEqualsAndSpaces(t *testing.T) {
	cases := map[string]string{
		`Unity -projectPath="/Users/ExampleUser/My Project" -useHub`:                                                             "/Users/ExampleUser/My Project",
		`Unity -projectpath '/Users/ExampleUser/Other Project' -flag`:                                                            "/Users/ExampleUser/Other Project",
		`Unity -projectPath /Users/ExampleUser/Plain -flag`:                                                                      "/Users/ExampleUser/Plain",
		`Unity Hub -- --silent -- -projectPath /Users/ExampleUser/SampleWorkspace/SampleUnityProject -cacheServerEnableUpload`:   "/Users/ExampleUser/SampleWorkspace/SampleUnityProject",
		`Unity -projectPath /Users/ExampleUser/SampleWorkspace/SampleUnityProject -acceptSoftwareTermsForThisRunOnly -useHub`:    "/Users/ExampleUser/SampleWorkspace/SampleUnityProject",
		`Unity -projectPath /Users/ExampleUser/SampleWorkspace/SampleUnityProject -cacheServerEnableDownload=false -useHub`:      "/Users/ExampleUser/SampleWorkspace/SampleUnityProject",
		`Unity -projectPath /Users/ExampleUser/SampleWorkspace/SampleUnityProject -hubSessionId 715810a5-220d-411e-a7d2-28cf46f`: "/Users/ExampleUser/SampleWorkspace/SampleUnityProject",
	}

	for command, expected := range cases {
		actual := extractProjectPath(command)
		if actual != expected {
			t.Fatalf("project path mismatch for %q: %q", command, actual)
		}
	}
}

func TestBuildFocusUnityProcessWindowsScriptThrowsOnFailures(t *testing.T) {
	script := buildFocusUnityProcessWindowsScript(123)

	for _, expected := range []string{
		"throw 'Unity process was not found: 123'",
		"throw 'Unity process has no main window handle: 123'",
		"throw 'Failed to show Unity window'",
		"$focused = $shell.AppActivate(123)",
		"throw 'Failed to focus Unity window'",
	} {
		if !strings.Contains(script, expected) {
			t.Fatalf("script missing %q: %s", expected, script)
		}
	}
	if strings.Contains(script, "catch { return }") || strings.Contains(script, "{ return }") {
		t.Fatalf("script should not silently return: %s", script)
	}
}
