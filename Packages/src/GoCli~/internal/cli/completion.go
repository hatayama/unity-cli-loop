package cli

import (
	"fmt"
	"io"
	"os"
	"path/filepath"
	"regexp"
	"runtime"
	"sort"
	"strings"
)

const (
	completionCommand        = "completion"
	listCommandsFlag         = "--list-commands"
	listOptionsFlag          = "--list-options"
	installCompletionFlag    = "--install"
	shellFlag                = "--shell"
	completionStartMarker    = "# >>> uloop completion >>>"
	completionEndMarker      = "# <<< uloop completion <<<"
	powerShellProfileSubpath = "Documents/WindowsPowerShell/Microsoft.PowerShell_profile.ps1"
	pwshProfileSubpath       = "Documents/PowerShell/Microsoft.PowerShell_profile.ps1"
)

var nativeCommandNames = []string{
	"completion",
	"fix",
	"focus-window",
	"launch",
	"list",
	"skills",
	"sync",
	"update",
}

func tryHandleCompletionRequest(args []string, cache toolsCache, stdout io.Writer, stderr io.Writer) (bool, int) {
	if len(args) == 0 {
		return false, 0
	}

	if args[0] == listCommandsFlag {
		printCommandNames(cache, stdout)
		return true, 0
	}

	if args[0] == listOptionsFlag {
		if len(args) < 2 {
			writeErrorEnvelope(stderr, (&argumentError{
				message:     "--list-options requires a command name",
				option:      listOptionsFlag,
				command:     completionCommand,
				nextActions: []string{"Pass the command name after `--list-options`."},
			}).toCLIError(errorContext{command: completionCommand}))
			return true, 1
		}
		printOptionsForCommand(args[1], cache, stdout)
		return true, 0
	}

	if args[0] != completionCommand {
		return false, 0
	}

	if len(args) == 2 && isHelpRequest(args[1:]) {
		printCompletionHelp(stdout)
		return true, 0
	}

	request, err := parseCompletionRequest(args[1:])
	if err != nil {
		writeClassifiedError(stderr, err, errorContext{command: completionCommand})
		return true, 1
	}

	shellName := request.shell
	if shellName == "" {
		shellName = detectShell()
	}
	if shellName == "" {
		writeErrorEnvelope(stderr, cliError{
			ErrorCode:   errorCodeInvalidArgument,
			Phase:       errorPhaseArgumentParsing,
			Message:     "Could not detect shell.",
			Retryable:   false,
			SafeToRetry: false,
			Command:     completionCommand,
			NextActions: []string{"Pass `--shell bash`, `--shell zsh`, `--shell powershell`, or `--shell pwsh`."},
		})
		return true, 1
	}

	script := getCompletionScript(shellName)
	if !request.install {
		writeLine(stdout, script)
		return true, 0
	}

	configPath, err := getShellConfigPath(shellName)
	if err != nil {
		writeClassifiedError(stderr, err, errorContext{command: completionCommand})
		return true, 1
	}
	if err := installCompletionScript(configPath, shellName, script); err != nil {
		writeClassifiedError(stderr, err, errorContext{command: completionCommand})
		return true, 1
	}

	writeFormat(stdout, "Completion installed to %s\n", configPath)
	if isPowerShellShell(shellName) {
		writeLine(stdout, "Restart PowerShell to enable completion.")
		return true, 0
	}
	writeFormat(stdout, "Run 'source %s' or restart your shell to enable completion.\n", configPath)
	return true, 0
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
				return completionRequest{}, missingValueArgumentError(shellFlag)
			}
			normalized, err := normalizeShell(args[index+1])
			if err != nil {
				return completionRequest{}, err
			}
			request.shell = normalized
			index++
			continue
		}

		return completionRequest{}, &argumentError{
			message:     "Unknown completion option: " + arg,
			option:      arg,
			command:     completionCommand,
			nextActions: []string{"Run `uloop completion --help` to inspect supported completion options."},
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
	return "", &argumentError{
		message:      "Unknown shell: " + value,
		option:       shellFlag,
		received:     value,
		expectedType: "bash|zsh|powershell|pwsh",
		command:      completionCommand,
		nextActions:  []string{"Use one of: bash, zsh, powershell, pwsh."},
	}
}

func printCommandNames(cache toolsCache, stdout io.Writer) {
	seen := map[string]bool{}
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
	writeLine(stdout, strings.Join(commands, "\n"))
}

func printOptionsForCommand(command string, cache toolsCache, stdout io.Writer) {
	for _, nativeCommand := range nativeCommandNames {
		if command == nativeCommand {
			return
		}
	}

	tool, ok := findTool(cache, command)
	if !ok {
		return
	}

	schema := tool.effectiveInputSchema()
	options := make([]string, 0, len(schema.Properties))
	for propertyName, property := range schema.Properties {
		options = append(options, "--"+optionNameForProperty(propertyName, property))
	}
	sort.Strings(options)
	writeLine(stdout, strings.Join(options, "\n"))
}

func detectShell() string {
	if runtime.GOOS == "windows" {
		return "powershell"
	}

	shellPath := os.Getenv("SHELL")
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

	return environmentHomeDirectory()
}

func getPwshProfilePath(home string, goos string) string {
	if goos == "windows" {
		return filepath.Join(home, filepath.FromSlash(pwshProfileSubpath))
	}
	return filepath.Join(home, ".config", "powershell", "Microsoft.PowerShell_profile.ps1")
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
	pattern := regexp.MustCompile(`(?s)\n?# >>> uloop completion >>>.*?# <<< uloop completion <<<\n?`)
	return pattern.ReplaceAllString(content, "")
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
	writeLine(stdout, "Usage:")
	writeLine(stdout, "  uloop completion [--shell bash|zsh|powershell|pwsh] [--install]")
}
