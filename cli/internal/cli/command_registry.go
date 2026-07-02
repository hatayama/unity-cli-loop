package cli

const (
	launchCommandName               = "launch"
	installCommandName              = "install"
	updateCommandName               = "update"
	uninstallCommandName            = "uninstall"
	skillsCommandName               = "skills"
	compileCommandName              = "compile"
	executeDynamicCodeCommandName   = "execute-dynamic-code"
	pausePointWaitCommandName       = "wait-for-pause-point"
	pausePointStatusUserCommandName = "pause-point-status"
	runTestsCommandName             = "run-tests"
)

type nativeCommandEntry struct {
	name        string
	description string
}

var nativeCommands = []nativeCommandEntry{
	{name: launchCommandName, description: "Open this Unity project with the matching Editor version"},
	{name: "list", description: "Show Unity tools currently exposed by the Editor"},
	{name: "sync", description: "Refresh .uloop/tools.json from the running Editor"},
	{name: "focus-window", description: "Bring the Unity Editor window to the foreground"},
	{name: pausePointWaitCommandName, description: "Wait until a named UloopPausePoint.Pause marker pauses Unity"},
	{name: pausePointStatusUserCommandName, description: "Show the state of a named UloopPausePoint.Pause marker"},
	{name: skillsCommandName, description: "List, install, or uninstall agent skills"},
	{name: "completion", description: "Print or install shell completion"},
	{name: installCommandName, description: "Configure the global uloop launcher binary"},
	{name: updateCommandName, description: "Update the global uloop launcher binary"},
	{name: uninstallCommandName, description: "Remove the global uloop launcher binary"},
}

func nativeCommandNamesForCompletion() []string {
	names := make([]string, 0, len(nativeCommands))
	for _, command := range nativeCommands {
		names = append(names, command.name)
	}
	return names
}
