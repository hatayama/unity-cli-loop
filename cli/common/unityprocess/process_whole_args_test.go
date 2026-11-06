package unityprocess

import (
	"encoding/base64"
	"slices"
	"testing"
)

const (
	macEditorForTest     = "/Applications/Unity/Hub/Editor/6000.0.0f1/Unity.app/Contents/MacOS/Unity"
	windowsEditorForTest = `C:\Editor\Unity.exe`
)

func parseWindowsCommandLineForTest(commandLine string) []UnityProcess {
	return parseWindowsUnityProcesses("123|" + base64.StdEncoding.EncodeToString([]byte(commandLine)) + "\r\n")
}

// Verifies a macOS Editor stays found, under its exact project path, when only a path contains a word that
// marks a batch mode run or an import worker. Losing it would make launch delete the project's lockfile and Temp
// and open a second Editor.
func TestMatchMacUnityProcessFindsEditorWhoseArgumentOnlyContainsTheWords(t *testing.T) {
	cases := []struct {
		name string
		args []string
		want string
	}{
		{"project path containing batchmode", []string{macEditorForTest, "-projectPath", "/work/nightly-batchmode"}, "/work/nightly-batchmode"},
		{"project path containing an import worker name", []string{macEditorForTest, "-projectPath", "/work/AssetImportWorker-lab"}, "/work/AssetImportWorker-lab"},
		{"project path with the flag as its own word", []string{macEditorForTest, "-projectPath", "/work/nightly -batchmode dir/game", "-useHub"}, "/work/nightly -batchmode dir/game"},
		{"log path containing batchmode", []string{macEditorForTest, "-projectPath", "/work/game", "-logFile", "/tmp/nightly-batchmode.log"}, "/work/game"},
		{"log path containing an import worker name", []string{macEditorForTest, "-projectPath", "/work/game", "-logFile", "/tmp/AssetImportWorker.log"}, "/work/game"},
		{"log path with the flag as its own word", []string{macEditorForTest, "-projectPath", "/work/game", "-logFile", "/tmp/nightly -batchmode dir/editor.log"}, "/work/game"},
		{"relative project path starting with an import worker name", []string{macEditorForTest, "-projectPath", "AssetImportWorker-lab"}, "AssetImportWorker-lab"},
		{"relative log path starting with an import worker name", []string{macEditorForTest, "-projectPath", "/work/game", "-logFile", "AssetImportWorker.log"}, "/work/game"},
		{"name that is not an import worker", []string{macEditorForTest, "-projectPath", "/work/game", "-name", "Nightly"}, "/work/game"},
		{"name flag with no value", []string{macEditorForTest, "-projectPath", "/work/game", "-name"}, "/work/game"},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			process, matched := matchMacUnityProcess(123, testCase.args)

			if !matched {
				t.Fatal("expected the Editor to be found")
			}
			if process.projectPath != testCase.want {
				t.Fatalf("project path = %q, want %q", process.projectPath, testCase.want)
			}
		})
	}
}

// Verifies a macOS batch mode run, an import worker, and a process that only names the Editor's path are left out.
// A shell listed as an Editor of unknown project would make status and launch's exit watch unable to tell it is gone.
func TestMatchMacUnityProcessSkipsBatchModeAndImportWorkers(t *testing.T) {
	cases := []struct {
		name string
		args []string
	}{
		{"batch mode", []string{macEditorForTest, "-batchmode", "-projectPath", "/work/game"}},
		{"batch mode in another case", []string{macEditorForTest, "-batchMode", "-projectPath", "/work/game"}},
		{"import worker", []string{macEditorForTest, "-projectPath", "/work/game", "-name", "AssetImportWorker0"}},
		{"import worker in another case", []string{macEditorForTest, "-projectPath", "/work/game", "-NAME", "assetimportworker0"}},
		{"import worker as the real command writes it", []string{macEditorForTest, "-adb2", "-batchMode", "-noUpm", "-name", "AssetImportWorker0", "-projectPath", "/work/game"}},
		{"shell running a batch mode command", []string{"/bin/zsh", "-c", macEditorForTest + " -batchmode -projectPath /work/game"}},
		{"shell naming the project", []string{"/bin/sh", "-c", macEditorForTest + " -projectPath /work/game; true"}},
		{"another program given the Editor's path", []string{"/usr/bin/tail", "-f", macEditorForTest}},
		{"no arguments", nil},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			if _, matched := matchMacUnityProcess(123, testCase.args); matched {
				t.Fatal("expected the process to be skipped")
			}
		})
	}
}

// Verifies a Windows Editor stays found when only a path contains those words, quoted or not.
func TestParseWindowsUnityProcessesFindsEditorWhoseArgumentOnlyContainsTheWords(t *testing.T) {
	cases := []struct {
		name        string
		commandLine string
		want        string
	}{
		{"unquoted project path containing batchmode", windowsEditorForTest + ` -projectPath C:\work\nightly-batchmode -useHub`, `C:\work\nightly-batchmode`},
		{"quoted project path with the flag as its own word", windowsEditorForTest + ` -projectPath "C:\work\nightly -batchmode dir\game" -useHub`, `C:\work\nightly -batchmode dir\game`},
		{"quoted project path containing an import worker name", windowsEditorForTest + ` -projectPath "C:\work\AssetImportWorker lab"`, `C:\work\AssetImportWorker lab`},
		{"log path with the flag as its own word", windowsEditorForTest + ` -projectPath "C:\work\game" -logFile "C:\logs\nightly -batchmode dir\editor.log"`, `C:\work\game`},
		{"relative log path starting with an import worker name", windowsEditorForTest + ` -projectPath "C:\work\game" -logFile AssetImportWorker.log`, `C:\work\game`},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			processes := parseWindowsCommandLineForTest(testCase.commandLine)

			if len(processes) != 1 {
				t.Fatalf("expected the Editor to be found, got %#v", processes)
			}
			if processes[0].projectPath != testCase.want {
				t.Fatalf("project path = %q, want %q", processes[0].projectPath, testCase.want)
			}
		})
	}
}

// Verifies a Windows batch mode run and an import worker are still left out, including a flag after a quoted path
// ending in a backslash, where a wrong quote reading would swallow the flag.
func TestParseWindowsUnityProcessesSkipsBatchModeAndImportWorkers(t *testing.T) {
	cases := []struct {
		name        string
		commandLine string
	}{
		{"batch mode", windowsEditorForTest + ` -batchmode -projectPath "C:\work\game"`},
		{"batch mode in another case", windowsEditorForTest + ` -batchMode -projectPath "C:\work\game"`},
		{"quoted batch mode", windowsEditorForTest + ` "-batchmode" -projectPath "C:\work\game"`},
		{"import worker", windowsEditorForTest + ` -projectPath "C:\work\game" -name AssetImportWorker0`},
		{"after a quoted path ending in a backslash", windowsEditorForTest + ` -projectPath "C:\my game\\" -batchmode`},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			if processes := parseWindowsCommandLineForTest(testCase.commandLine); len(processes) != 0 {
				t.Fatalf("expected the process to be skipped, got %#v", processes)
			}
		})
	}
}

// Verifies a Windows command line splits the way programs read it. Only a backslash before a double quote escapes,
// so a quoted path ending in a backslash still closes its quote and the argument after it stays separate.
func TestSplitWindowsCommandLine(t *testing.T) {
	cases := []struct {
		name        string
		commandLine string
		want        []string
	}{
		{"plain arguments", `a  b	c`, []string{"a", "b", "c"}},
		{"surrounding whitespace", `  a b  `, []string{"a", "b"}},
		{"quoted argument with a space", `"a b" c`, []string{"a b", "c"}},
		{"quote inside an argument", `-x="a b" c`, []string{"-x=a b", "c"}},
		{"backslashes without a quote are literal", `C:\dir\file.exe -x`, []string{`C:\dir\file.exe`, "-x"}},
		{"backslash before a space", `a\ b`, []string{`a\`, "b"}},
		{"trailing backslash", `a b\`, []string{"a", `b\`}},
		{"escaped backslash closes the quote", `"a b\\" c`, []string{`a b\`, "c"}},
		{"escaped quote stays in the argument", `a\"b c`, []string{`a"b`, "c"}},
		{"empty command line", ``, nil},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			if got := splitWindowsCommandLine(testCase.commandLine); !slices.Equal(got, testCase.want) {
				t.Fatalf("got %#v, want %#v", got, testCase.want)
			}
		})
	}
}
