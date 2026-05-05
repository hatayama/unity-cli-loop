package dispatcher

import (
	_ "embed"
	"encoding/json"
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

//go:embed default-tools.json
var embeddedDefaultTools []byte

var nativeCommandOptions = map[string][]string{
	completionCommand: {installCompletionFlag, shellFlag},
	launchCommandName: {
		"--" + projectPathFlagName,
		"--delete-recovery",
		"--max-depth",
		"--platform",
		"--quit",
		"--restart",
	},
	updateCommandName: {},
}

type completionRequest struct {
	install bool
	shell   string
}

func tryHandleCompletionRequest(args []string, stdout io.Writer, stderr io.Writer) (bool, int) {
	if len(args) == 0 {
		return false, 0
	}

	if args[0] == listCommandsFlag {
		printCompletionCommandNames(stdout)
		return true, 0
	}

	if args[0] == listOptionsFlag {
		if len(args) < 2 {
			writeError(stderr, argumentError("--list-options requires a command name", completionCommand))
			return true, 1
		}
		printCompletionOptions(args[1], stdout)
		return true, 0
	}

	if args[0] != completionCommand {
		return false, 0
	}
	if len(args) >= 2 && args[1] == listCommandsFlag {
		printCompletionCommandNames(stdout)
		return true, 0
	}
	if len(args) >= 2 && args[1] == listOptionsFlag {
		if len(args) < 3 {
			writeError(stderr, argumentError("--list-options requires a command name", completionCommand))
			return true, 1
		}
		printCompletionOptions(args[2], stdout)
		return true, 0
	}

	if len(args) == 2 && isHelpRequest(args[1:]) {
		printCompletionHelp(stdout)
		return true, 0
	}

	request, err := parseCompletionRequest(args[1:])
	if err != nil {
		writeError(stderr, argumentError(err.Error(), completionCommand))
		return true, 1
	}

	shellName := request.shell
	if shellName == "" {
		shellName = detectShell()
	}
	if shellName == "" {
		writeError(stderr, argumentError("Could not detect shell.", completionCommand))
		return true, 1
	}

	script := getCompletionScript(shellName)
	if !request.install {
		writeLine(stdout, script)
		return true, 0
	}

	configPath, err := getShellConfigPath(shellName)
	if err != nil {
		writeError(stderr, internalError(err.Error(), ""))
		return true, 1
	}
	if err := installCompletionScript(configPath, shellName, script); err != nil {
		writeError(stderr, internalError(err.Error(), ""))
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
				return completionRequest{}, fmt.Errorf("%s requires a value", shellFlag)
			}
			normalized, err := normalizeShell(args[index+1])
			if err != nil {
				return completionRequest{}, err
			}
			request.shell = normalized
			index++
			continue
		}

		return completionRequest{}, fmt.Errorf("unknown completion option: %s", arg)
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
	return "", fmt.Errorf("unknown shell: %s", value)
}

func printCompletionCommandNames(stdout io.Writer) {
	seen := map[string]bool{}
	commands := []string{}
	for _, entry := range nativeCommands {
		if seen[entry.name] {
			continue
		}
		seen[entry.name] = true
		commands = append(commands, entry.name)
	}
	if cache, ok := loadCompletionCache(); ok {
		for _, tool := range cache.Tools {
			if seen[tool.Name] {
				continue
			}
			seen[tool.Name] = true
			commands = append(commands, tool.Name)
		}
	}
	sort.Strings(commands)
	writeLine(stdout, strings.Join(commands, "\n"))
}

func printCompletionOptions(command string, stdout io.Writer) {
	options, ok := nativeCommandOptions[command]
	if ok {
		writeLine(stdout, strings.Join(options, "\n"))
		return
	}

	cache, ok := loadCompletionCache()
	if !ok {
		return
	}
	for _, tool := range cache.Tools {
		if tool.Name != command {
			continue
		}
		printToolOptions(tool, stdout)
		return
	}
}

func printToolOptions(tool cachedTool, stdout io.Writer) {
	schema := tool.InputSchema
	if len(schema.Properties) == 0 {
		schema = tool.ParameterSchema
	}
	options := make([]string, 0, len(schema.Properties))
	for propertyName, property := range schema.Properties {
		options = append(options, "--"+optionNameForProperty(propertyName, property))
	}
	sort.Strings(options)
	writeLine(stdout, strings.Join(options, "\n"))
}

func optionNameForProperty(propertyName string, property cachedToolProperty) string {
	kebabName := pascalToKebab(propertyName)
	if isNegatedBooleanProperty(property) {
		return "no-" + kebabName
	}
	return kebabName
}

func isNegatedBooleanProperty(property cachedToolProperty) bool {
	defaultValue, ok := effectiveDefault(property).(bool)
	return strings.EqualFold(property.Type, "boolean") && ok && defaultValue
}

func effectiveDefault(property cachedToolProperty) any {
	if property.Default != nil {
		return property.Default
	}
	return property.DefaultValue
}

func pascalToKebab(value string) string {
	if value == "" {
		return value
	}

	var builder strings.Builder
	for index, character := range value {
		if index > 0 && character >= 'A' && character <= 'Z' {
			builder.WriteByte('-')
		}
		builder.WriteRune(character)
	}
	return strings.ToLower(builder.String())
}

func loadCompletionCache() (cachedTools, bool) {
	startPath, err := os.Getwd()
	if err != nil {
		return loadDefaultCompletionCache()
	}
	projectRoot, err := resolveProjectRoot(startPath, "", []string{"help"})
	if err != nil {
		return loadDefaultCompletionCache()
	}
	cache, ok := loadCachedTools(projectRoot)
	if ok {
		return cache, true
	}
	return loadDefaultCompletionCache()
}

func loadDefaultCompletionCache() (cachedTools, bool) {
	var cache cachedTools
	if json.Unmarshal(embeddedDefaultTools, &cache) != nil {
		return cachedTools{}, false
	}
	return cache, true
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
