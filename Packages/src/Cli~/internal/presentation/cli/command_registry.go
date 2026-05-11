package cli

type nativeCommandEntry struct {
	name        string
	description string
}

var nativeCommands = []nativeCommandEntry{
	{name: "launch", description: "Open this Unity project with the matching Editor version"},
	{name: "list", description: "Show Unity tools currently exposed by the Editor"},
	{name: "sync", description: "Refresh .uloop/tools.json from the running Editor"},
	{name: "focus-window", description: "Bring the Unity Editor window to the foreground"},
	{name: "fix", description: "Remove stale uloop lock files after an interrupted run"},
	{name: "skills", description: "List, install, or uninstall agent skills"},
	{name: "completion", description: "Print or install shell completion"},
	{name: "update", description: "Update the global uloop launcher binary"},
}

func nativeCommandNamesForCompletion() []string {
	names := make([]string, 0, len(nativeCommands))
	for _, command := range nativeCommands {
		names = append(names, command.name)
	}
	return names
}
