package dispatcher

import (
	"github.com/hatayama/unity-cli-loop/common/clicore"
	"github.com/hatayama/unity-cli-loop/common/tooldocs"
)

// nativeCommandOptions lists the static --help options for native commands
// whose help the dispatcher answers itself. Only dispatcher-owned commands
// remain here, since runner-owned commands forward --help to the pinned
// runner instead (see command_help.go).
var nativeCommandOptions = map[string][]string{
	clicore.LaunchCommandName: {
		"--" + tooldocs.ProjectPathFlagName,
		"--delete-recovery",
		"--editor-version",
		"--max-depth",
		"--platform",
		"--quit",
		"--restart",
	},
	clicore.InstallCommandName: {"--" + installDirFlagName},
	clicore.UpdateCommandName:  {"--" + updateToVersionFlagName},
	clicore.VersionCommandName: {"--json"},
}
