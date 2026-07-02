package clicore

const (
	LaunchCommandName               = "launch"
	InstallCommandName              = "install"
	UpdateCommandName               = "update"
	UninstallCommandName            = "uninstall"
	SkillsCommandName               = "skills"
	CompileCommandName              = "compile"
	ExecuteDynamicCodeCommandName   = "execute-dynamic-code"
	PausePointWaitCommandName       = "wait-for-pause-point"
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
}

var NativeCommands = []NativeCommandEntry{
	{Name: LaunchCommandName, Description: "Open this Unity project with the matching Editor version"},
	{Name: "list", Description: "Show Unity tools currently exposed by the Editor"},
	{Name: "sync", Description: "Refresh .uloop/tools.json from the running Editor"},
	{Name: "focus-window", Description: "Bring the Unity Editor window to the foreground"},
	{Name: PausePointWaitCommandName, Description: "Wait until a named UloopPausePoint.Pause marker pauses Unity"},
	{Name: PausePointStatusUserCommandName, Description: "Show the state of a named UloopPausePoint.Pause marker"},
	{Name: SkillsCommandName, Description: "List, install, or uninstall agent skills"},
	{Name: "completion", Description: "Print or install shell completion"},
	{Name: InstallCommandName, Description: "Configure the global uloop launcher binary"},
	{Name: UpdateCommandName, Description: "Update the global uloop launcher binary"},
	{Name: UninstallCommandName, Description: "Remove the global uloop launcher binary"},
}

func NativeCommandNamesForCompletion() []string {
	names := make([]string, 0, len(NativeCommands))
	for _, command := range NativeCommands {
		names = append(names, command.Name)
	}
	return names
}
