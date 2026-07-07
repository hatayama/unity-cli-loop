package dispatcher

import (
	"fmt"
	"io"
	"os"
	"os/exec"
	"path/filepath"
	"regexp"
	"runtime"
	"sort"
	"strings"

	"github.com/hatayama/unity-cli-loop/common/clicore"
)

const (
	installCompletionFlag    = "--install"
	shellFlag                = "--shell"
	completionStartMarker    = "# >>> uloop completion >>>"
	completionEndMarker      = "# <<< uloop completion <<<"
	powerShellProfileSubpath = "Documents/WindowsPowerShell/Microsoft.PowerShell_profile.ps1"
	pwshProfileSubpath       = "Documents/PowerShell/Microsoft.PowerShell_profile.ps1"
)

var completionBlockPattern = regexp.MustCompile(`(?s)\n?# >>> uloop completion >>>.*?# <<< uloop completion <<<\n?`)

func tryHandleCompletionRequest(args []string, cache clicore.ToolsCache, stdout io.Writer, stderr io.Writer) (bool, int) {
	if len(args) == 0 {
		return false, 0
	}

	if handled, code := tryHandleCompletionListRequest(args, cache, stdout, stderr); handled {
		return true, code
	}

	if args[0] != clicore.CompletionCommand {
		return false, 0
	}
	return true, runCompletionCommand(args[1:], cache, stdout, stderr)
}

func tryHandleCompletionListRequest(args []string, cache clicore.ToolsCache, stdout io.Writer, stderr io.Writer) (bool, int) {
	if len(args) == 0 {
		return false, 0
	}

	switch args[0] {
	case clicore.ListCommandsFlag:
		printCommandNames(cache, stdout)
		return true, 0
	case clicore.ListOptionsFlag:
		if len(args) < 2 {
			writeMissingCompletionCommandName(stderr)
			return true, 1
		}
		printOptionsForCommand(args[1], cache, stdout)
		return true, 0
	default:
		return false, 0
	}
}

func runCompletionCommand(args []string, cache clicore.ToolsCache, stdout io.Writer, stderr io.Writer) int {
	if handled, code := tryHandleCompletionListRequest(args, cache, stdout, stderr); handled {
		return code
	}

	if clicore.ContainsHelpRequest(args) {
		printCompletionHelp(stdout)
		return 0
	}

	request, err := parseCompletionRequest(args)
	if err != nil {
		clicore.WriteClassifiedError(stderr, err, clicore.ErrorContext{Command: clicore.CompletionCommand})
		return 1
	}
	return runCompletionRequest(request, stdout, stderr)
}

func runCompletionRequest(request completionRequest, stdout io.Writer, stderr io.Writer) int {
	shellName := request.shell
	if shellName == "" {
		shellName = detectShell()
	}
	if shellName == "" {
		writeCompletionShellDetectionError(stderr)
		return 1
	}

	script := getCompletionScript(shellName)
	if !request.install {
		clicore.WriteLine(stdout, script)
		return 0
	}

	configPath, err := getShellConfigPath(shellName)
	if err != nil {
		clicore.WriteClassifiedError(stderr, err, clicore.ErrorContext{Command: clicore.CompletionCommand})
		return 1
	}
	if err := installCompletionScript(configPath, shellName, script); err != nil {
		clicore.WriteClassifiedError(stderr, err, clicore.ErrorContext{Command: clicore.CompletionCommand})
		return 1
	}

	writeCompletionInstallResult(stdout, shellName, configPath)
	return 0
}

func writeMissingCompletionCommandName(stderr io.Writer) {
	clicore.WriteErrorEnvelope(stderr, (&clicore.ArgumentError{
		Message:     "--list-options requires a command name",
		Option:      clicore.ListOptionsFlag,
		Command:     clicore.CompletionCommand,
		NextActions: []string{"Pass the command name after `--list-options`."},
	}).ToCLIError(clicore.ErrorContext{Command: clicore.CompletionCommand}))
}

func writeCompletionShellDetectionError(stderr io.Writer) {
	clicore.WriteErrorEnvelope(stderr, clicore.CLIError{
		ErrorCode:   clicore.ErrorCodeInvalidArgument,
		Phase:       clicore.ErrorPhaseArgumentParsing,
		Message:     "Could not detect shell.",
		Retryable:   false,
		SafeToRetry: false,
		Command:     clicore.CompletionCommand,
		NextActions: []string{"Pass `--shell bash`, `--shell zsh`, `--shell powershell`, or `--shell pwsh`."},
	})
}

func writeCompletionInstallResult(stdout io.Writer, shellName string, configPath string) {
	clicore.WriteFormat(stdout, "Completion installed to %s\n", configPath)
	if isPowerShellShell(shellName) {
		clicore.WriteLine(stdout, "Restart PowerShell to enable completion.")
		return
	}
	clicore.WriteFormat(stdout, "Run 'source %s' or restart your shell to enable completion.\n", configPath)
}

type completionRequest struct {
	install bool
	shell   string
}

func parseCompletionRequest(args []string) (completionRequest, error) {
	request := completionRequest{}
	for index := 0; index < len(args); index++ {
		arg := args[index]
		if arg == installCompletionFlag {
			request.install = true
			continue
		}

		if strings.HasPrefix(arg, shellFlag+"=") {
			normalized, err := normalizeShell(strings.TrimPrefix(arg, shellFlag+"="))
			if err != nil {
				return completionRequest{}, err
			}
			request.shell = normalized
			continue
		}

		if arg == shellFlag {
			if index+1 >= len(args) {
				return completionRequest{}, clicore.MissingValueArgumentError(shellFlag)
			}
			normalized, err := normalizeShell(args[index+1])
			if err != nil {
				return completionRequest{}, err
			}
			request.shell = normalized
			index++
			continue
		}

		return completionRequest{}, &clicore.ArgumentError{
			Message:     "Unknown completion option: " + arg,
			Option:      arg,
			Command:     clicore.CompletionCommand,
			NextActions: []string{"Run `uloop completion --help` to inspect supported completion options."},
		}
	}
	return request, nil
}

func normalizeShell(value string) (string, error) {
	normalized := strings.ToLower(value)
	if normalized == "bash" || normalized == "zsh" || normalized == "powershell" || normalized == "pwsh" {
		return normalized, nil
	}
	if normalized == "powershell-core" {
		return "pwsh", nil
	}
	return "", &clicore.ArgumentError{
		Message:      "Unknown shell: " + value,
		Option:       shellFlag,
		Received:     value,
		ExpectedType: "bash|zsh|powershell|pwsh",
		Command:      clicore.CompletionCommand,
		NextActions:  []string{"Use one of: bash, zsh, powershell, pwsh."},
	}
}

func printCommandNames(cache clicore.ToolsCache, stdout io.Writer) {
	seen := map[string]bool{}
	nativeCommandNames := clicore.NativeCommandNamesForCompletion()
	commands := make([]string, 0, len(nativeCommandNames)+len(cache.Tools))
	for _, command := range nativeCommandNames {
		if seen[command] {
			continue
		}
		seen[command] = true
		commands = append(commands, command)
	}
	for _, tool := range cache.Tools {
		if seen[tool.Name] {
			continue
		}
		seen[tool.Name] = true
		commands = append(commands, tool.Name)
	}
	sort.Strings(commands)
	clicore.WriteLine(stdout, strings.Join(commands, "\n"))
}

func printOptionsForCommand(command string, cache clicore.ToolsCache, stdout io.Writer) {
	nativeOptions, ok := nativeCommandOptions[command]
	if ok {
		options := append([]string{}, nativeOptions...)
		sort.Strings(options)
		clicore.WriteLine(stdout, strings.Join(options, "\n"))
		return
	}
	if _, ok := clicore.NativeCommand(command); ok {
		clicore.WriteLine(stdout, "")
		return
	}
	if command == clicore.ExecuteDynamicCodeCommandName {
		tool, ok := clicore.FindDefaultTool(command)
		if !ok {
			return
		}
		printOptionsForTool(tool, stdout)
		return
	}

	tool, ok := clicore.FindTool(cache, command)
	if !ok {
		return
	}

	printOptionsForTool(tool, stdout)
}

func printOptionsForTool(tool clicore.ToolDefinition, stdout io.Writer) {
	clicore.WriteLine(stdout, strings.Join(clicore.VisibleOptionNamesForTool(tool), "\n"))
}

func detectShell() string {
	return detectShellForPlatform(runtime.GOOS, os.Getenv("SHELL"), os.Getenv("MSYSTEM"), exec.LookPath)
}

func detectShellFromEnvironment(goos string, shellPath string, msystem string) string {
	return detectShellForPlatform(goos, shellPath, msystem, exec.LookPath)
}

func detectShellForPlatform(goos string, shellPath string, msystem string, lookPath func(string) (string, error)) string {
	shellName := detectShellName(shellPath)
	if goos == "windows" {
		if isPosixShell(shellName) && msystem != "" {
			return shellName
		}
		if shellName == "pwsh" || shellName == "powershell" {
			return shellName
		}
		if _, err := lookPath("pwsh"); err == nil {
			return "pwsh"
		}
		if _, err := lookPath("powershell"); err == nil {
			return "powershell"
		}
		return ""
	}

	return shellName
}

func detectShellName(shellPath string) string {
	shellPath = strings.ToLower(shellPath)
	if strings.Contains(shellPath, "pwsh") {
		return "pwsh"
	}
	if strings.Contains(shellPath, "powershell") {
		return "powershell"
	}
	if strings.Contains(shellPath, "zsh") {
		return "zsh"
	}
	if strings.Contains(shellPath, "bash") {
		return "bash"
	}
	return ""
}

func getShellConfigPath(shellName string) (string, error) {
	home, err := getHomeDirectoryForShell(shellName, runtime.GOOS, getHomeDirectory, os.UserHomeDir)
	if err != nil {
		return "", err
	}

	switch shellName {
	case "zsh":
		return filepath.Join(home, ".zshrc"), nil
	case "bash":
		return filepath.Join(home, ".bashrc"), nil
	case "powershell":
		return filepath.Join(home, filepath.FromSlash(powerShellProfileSubpath)), nil
	case "pwsh":
		return getPwshProfilePath(home, runtime.GOOS), nil
	default:
		return "", fmt.Errorf("unknown shell: %s", shellName)
	}
}

func getHomeDirectory() (string, error) {
	home := os.Getenv("HOME")
	if home != "" {
		return home, nil
	}

	return os.UserHomeDir()
}

func getHomeDirectoryForShell(
	shellName string,
	goos string,
	environmentHomeDirectory func() (string, error),
	userHomeDirectory func() (string, error),
) (string, error) {
	if goos == "windows" && isPowerShellShell(shellName) {
		return userHomeDirectory()
	}

	home, err := environmentHomeDirectory()
	if err != nil {
		return "", err
	}
	if goos == "windows" && isPosixShell(shellName) {
		return normalizeWindowsPosixHomeDirectory(home), nil
	}
	return home, nil
}

func getPwshProfilePath(home string, goos string) string {
	if goos == "windows" {
		return filepath.Join(home, filepath.FromSlash(pwshProfileSubpath))
	}
	return filepath.Join(home, ".config", "powershell", "Microsoft.PowerShell_profile.ps1")
}

func isPosixShell(shellName string) bool {
	return shellName == "bash" || shellName == "zsh"
}

func normalizeWindowsPosixHomeDirectory(home string) string {
	if home == "" {
		return home
	}
	if len(home) >= 3 && home[0] == '/' && isASCIIAlpha(home[1]) && home[2] == '/' {
		return windowsDrivePath(home[1], home[3:])
	}
	if len(home) >= 7 && strings.HasPrefix(home, "/mnt/") && isASCIIAlpha(home[5]) && home[6] == '/' {
		return windowsDrivePath(home[5], home[7:])
	}
	return home
}

func windowsDrivePath(driveLetter byte, rest string) string {
	drive := string(toUpperASCIILetter(driveLetter)) + `:\`
	if rest == "" {
		return drive
	}
	return drive + strings.ReplaceAll(rest, "/", `\`)
}

func isASCIIAlpha(value byte) bool {
	return (value >= 'A' && value <= 'Z') || (value >= 'a' && value <= 'z')
}

func toUpperASCIILetter(value byte) byte {
	if value >= 'a' && value <= 'z' {
		return value - ('a' - 'A')
	}
	return value
}

func installCompletionScript(configPath string, shellName string, script string) error {
	if err := os.MkdirAll(filepath.Dir(configPath), 0o755); err != nil {
		return err
	}

	content := ""
	if existing, err := os.ReadFile(configPath); err == nil {
		content = string(existing)
	}

	content = removeExistingCompletionBlock(content)
	lineToAdd := "\n" + completionStartMarker + "\n"
	if isPowerShellShell(shellName) {
		lineToAdd += script + "\n"
	} else {
		lineToAdd += fmt.Sprintf("eval \"$(uloop completion --shell %s)\"\n", shellName)
	}
	lineToAdd += completionEndMarker + "\n"
	return os.WriteFile(configPath, []byte(content+lineToAdd), 0o644)
}

func removeExistingCompletionBlock(content string) string {
	return completionBlockPattern.ReplaceAllString(content, "")
}

func getCompletionScript(shellName string) string {
	switch shellName {
	case "bash":
		return `# uloop bash completion
_uloop_completions() {
  local cur="${COMP_WORDS[COMP_CWORD]}"
  local cmd="${COMP_WORDS[1]}"

  if [[ ${COMP_CWORD} -eq 1 ]]; then
    COMPREPLY=($(compgen -W "$(uloop --list-commands 2>/dev/null)" -- "${cur}"))
  elif [[ ${COMP_CWORD} -ge 2 ]]; then
    COMPREPLY=($(compgen -W "$(uloop --list-options ${cmd} 2>/dev/null)" -- "${cur}"))
  fi
}
complete -F _uloop_completions uloop`
	case "powershell", "pwsh":
		return `# uloop PowerShell completion
Register-ArgumentCompleter -Native -CommandName uloop -ScriptBlock {
  param($wordToComplete, $commandAst, $cursorPosition)
  $commands = $commandAst.CommandElements
  if ($commands.Count -eq 1) {
    uloop --list-commands 2>$null | Where-Object { $_ -like "$wordToComplete*" } | ForEach-Object {
      [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterValue', $_)
    }
  } elseif ($commands.Count -ge 2) {
    $cmd = $commands[1].ToString()
    uloop --list-options $cmd 2>$null | Where-Object { $_ -like "$wordToComplete*" } | ForEach-Object {
      [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterValue', $_)
    }
  }
}`
	default:
		return `# uloop zsh completion
_uloop() {
  local -a commands
  local -a options
  local -a used_options

  if (( CURRENT == 2 )); then
    commands=(${(f)"$(uloop --list-commands 2>/dev/null)"})
    _describe 'command' commands
  elif (( CURRENT >= 3 )); then
    options=(${(f)"$(uloop --list-options ${words[2]} 2>/dev/null)"})
    used_options=(${words:2})
    for opt in ${used_options}; do
      options=(${options:#$opt})
    done
    _describe 'option' options
  fi
}
compdef _uloop uloop`
	}
}

func isPowerShellShell(shellName string) bool {
	return shellName == "powershell" || shellName == "pwsh"
}

func printCompletionHelp(stdout io.Writer) {
	clicore.WriteLine(stdout, "Usage:")
	clicore.WriteLine(stdout, "  uloop completion [--shell bash|zsh|powershell|pwsh] [--install]")
	clicore.WriteLine(stdout, "")
	clicore.WriteLine(stdout, "Completion helpers:")
	clicore.WriteLine(stdout, "  uloop --list-commands           Print command names for completion")
	clicore.WriteLine(stdout, "  uloop --list-options <command>  Print options for a command")
}
