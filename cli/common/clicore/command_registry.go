package clicore

const (
	LaunchCommandName               = "launch"
	InstallCommandName              = "install"
	UpdateCommandName               = "update"
	UninstallCommandName            = "uninstall"
	SkillsCommandName               = "skills"
	CompileCommandName              = "compile"
	ExecuteDynamicCodeCommandName   = "execute-dynamic-code"
	PausePointAwaitCommandName      = "await-pause-point"
	PausePointStatusUserCommandName = "pause-point-status"
	RunTestsCommandName             = "run-tests"
	CompletionCommand               = "completion"
	ListCommandsFlag                = "--list-commands"
	ListOptionsFlag                 = "--list-options"
	PausePointIDFlagName            = "id"
	PausePointTimeoutFlagName       = "timeout-seconds"
	PausePointLogsMaxCountFlagName  = "matching-logs-max-count"
)

type NativeCommandEntry struct {
	Name        string
	Description string
	Owner       CommandOwner
}

type CommandOwner string

const (
	DispatcherOwned CommandOwner = "dispatcher"
	RunnerOwned     CommandOwner = "runner"
)

var NativeCommands = []NativeCommandEntry{
	{Name: LaunchCommandName, Description: "Open this Unity project with the matching Editor version", Owner: DispatcherOwned},
	{Name: "list", Description: "Show Unity tools currently exposed by the Editor", Owner: RunnerOwned},
	{Name: "sync", Description: "Refresh .uloop/tools.json from the running Editor", Owner: RunnerOwned},
	{Name: "focus-window", Description: "Bring the Unity Editor window to the foreground", Owner: RunnerOwned},
	{Name: PausePointAwaitCommandName, Description: "Wait until a named UloopPausePoint.Pause marker pauses Unity", Owner: RunnerOwned},
	{Name: PausePointStatusUserCommandName, Description: "Show the state of a named UloopPausePoint.Pause marker", Owner: RunnerOwned},
	{Name: SkillsCommandName, Description: "List, install, or uninstall agent skills", Owner: DispatcherOwned},
	{Name: CompletionCommand, Description: "Print or install shell completion", Owner: DispatcherOwned},
	{Name: InstallCommandName, Description: "Configure the global uloop launcher binary", Owner: DispatcherOwned},
	{Name: UpdateCommandName, Description: "Update the global uloop launcher binary", Owner: DispatcherOwned},
	{Name: UninstallCommandName, Description: "Remove the global uloop launcher binary", Owner: DispatcherOwned},
}

// IsDispatcherOwnedCommandName reports whether a native command belongs to the
// global launcher's process. This is the single source of truth for the
// dispatcher/runner command split: the dispatcher handles these in-process, and
// the project runner must reject them instead of executing them.
func IsDispatcherOwnedCommandName(command string) bool {
	owner, ok := NativeCommandOwner(command)
	return ok && owner == DispatcherOwned
}

func IsRunnerOwnedCommandName(command string) bool {
	owner, ok := NativeCommandOwner(command)
	return ok && owner == RunnerOwned
}

func NativeCommandOwner(command string) (CommandOwner, bool) {
	entry, ok := NativeCommand(command)
	if !ok {
		return "", false
	}
	return entry.Owner, true
}

func NativeCommand(command string) (NativeCommandEntry, bool) {
	for _, entry := range NativeCommands {
		if entry.Name == command {
			return entry, true
		}
	}
	return NativeCommandEntry{}, false
}

func NativeCommandNamesForCompletion() []string {
	names := make([]string, 0, len(NativeCommands))
	for _, command := range NativeCommands {
		names = append(names, command.Name)
	}
	return names
}
