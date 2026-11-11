package unityprocess

import (
	"encoding/base64"
	"errors"
	"testing"
)

const windowsSampleProjectPath = `C:\Projects\Sample`

func windowsProcessListLine(pid string, commandLine string) string {
	return pid + "|" + base64.StdEncoding.EncodeToString([]byte(commandLine)) + "\r\n"
}

// Verifies Unity Hub's helper process is left out of the Editor list, while an Editor whose path or project
// merely contains a "resources" directory, or whose first argument is a flag, is still listed.
func TestParseWindowsUnityProcessesLeavesOutUnityHubHelper(t *testing.T) {
	cases := []struct {
		name            string
		commandLine     string
		wantListed      bool
		wantProjectPath string
	}{
		{
			name:        "measured Hub helper command line",
			commandLine: `"C:\Program Files\Unity Hub\resources\unity.exe" serve`,
		},
		{
			name:        "Hub helper with other casing and no quotes",
			commandLine: `C:\Hub\Resources\Unity.exe serve`,
		},
		{
			name:        "Hub helper with forward slashes",
			commandLine: `C:/Hub/resources/unity.exe serve`,
		},
		{
			name:        "Editor without arguments",
			commandLine: `"C:\Program Files\Unity\Hub\Editor\2022.3.0f1\Editor\Unity.exe"`,
			wantListed:  true,
		},
		{
			name:            "resources is not the directory right above the executable",
			commandLine:     `"C:\resources\Unity\Editor\Unity.exe" -projectPath "C:\Projects\Sample"`,
			wantListed:      true,
			wantProjectPath: windowsSampleProjectPath,
		},
		{
			name:        "argument that is not a flag after an executable not right under resources",
			commandLine: `"C:\resources\Unity\Editor\Unity.exe" serve`,
			wantListed:  true,
		},
		{
			name:            "project path ends in resources",
			commandLine:     `"C:\Unity\Editor\Unity.exe" -projectPath "C:\Game\resources"`,
			wantListed:      true,
			wantProjectPath: `C:\Game\resources`,
		},
		{
			name:            "executable under resources whose first argument is a flag",
			commandLine:     `"C:\Tools\resources\Unity.exe" -projectPath "C:\Projects\Sample"`,
			wantListed:      true,
			wantProjectPath: windowsSampleProjectPath,
		},
		{
			name:        "executable under resources without arguments",
			commandLine: `"C:\Tools\resources\Unity.exe"`,
			wantListed:  true,
		},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			processes := parseWindowsUnityProcesses(windowsProcessListLine("123", testCase.commandLine))

			if !testCase.wantListed {
				if len(processes) != 0 {
					t.Fatalf("expected the Hub helper to be left out, got %#v", processes)
				}
				return
			}
			if len(processes) != 1 || processes[0].Pid != 123 || processes[0].projectPath != testCase.wantProjectPath {
				t.Fatalf("expected one Editor with project %q, got %#v", testCase.wantProjectPath, processes)
			}
		})
	}
}

// Verifies a Unity.exe whose command line could not be read (an empty encoded field) is listed as an Editor
// whose project is unknown, instead of being dropped as if no Editor were running.
func TestParseWindowsUnityProcessesListsAnUnreadableCommandLineAsUnidentified(t *testing.T) {
	processes := parseWindowsUnityProcesses("4321|\r\n")

	if len(processes) != 1 || processes[0].Pid != 4321 || processes[0].projectPath != "" {
		t.Fatalf("expected one Editor with pid 4321 and no project path, got %#v", processes)
	}
}

// Verifies how the project lookups treat a listed Unity.exe with an unreadable command line and a listed
// Hub helper: the unreadable one may be the project's Editor, while the Hub helper never counts as an Editor.
func TestSelectProjectProcessWithUnreadableCommandLineAndHubHelper(t *testing.T) {
	unreadableLine := "4321|\r\n"
	projectEditorLine := windowsProcessListLine("123", `"C:\Unity\Editor\Unity.exe" -projectPath "C:\Projects\Sample"`)
	hubHelperLine := windowsProcessListLine("555", `"C:\Program Files\Unity Hub\resources\unity.exe" serve`)
	cases := []struct {
		name         string
		output       string
		definitively bool
		wantPid      int
		wantErr      error
	}{
		{
			name:         "project Editor beside an unreadable one is found",
			output:       unreadableLine + projectEditorLine,
			definitively: true,
			wantPid:      123,
		},
		{
			name:         "unreadable one alone may be the project's Editor",
			output:       unreadableLine,
			definitively: true,
			wantErr:      ErrUnidentifiedUnityEditor,
		},
		{
			name:   "unreadable one alone is not reported by the plain lookup",
			output: unreadableLine,
		},
		{
			name:         "Hub helper alone is no Editor",
			output:       hubHelperLine,
			definitively: true,
		},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			processes := parseWindowsUnityProcesses(testCase.output)

			var process *UnityProcess
			var err error
			if testCase.definitively {
				process, err = selectProjectProcessDefinitively(processes, windowsSampleProjectPath)
			} else {
				process, _, err = selectProjectProcess(processes, windowsSampleProjectPath)
			}

			gotPid := 0
			if process != nil {
				gotPid = process.Pid
			}
			if gotPid != testCase.wantPid || !errors.Is(err, testCase.wantErr) || (testCase.wantErr == nil && err != nil) {
				t.Fatalf("pid = %d, err = %v, want pid = %d, err = %v", gotPid, err, testCase.wantPid, testCase.wantErr)
			}
		})
	}
}
