package projectrunner

import (
	"fmt"
	"strings"

	clierrors "github.com/hatayama/unity-cli-loop/common/errors"

	"github.com/hatayama/unity-cli-loop/common/clicore"
	"github.com/hatayama/unity-cli-loop/common/tooldocs"
)

// pausePointFlagOwnerSearchOrder fixes the order in which an unknown flag's owning command is
// resolved. Several pause-point commands accept the same flag name (--id and --timeout-seconds
// exist on more than one), so a map-order search would report a different owner from run to run.
// enable-pause-point comes first because it owns the largest flag set and is the command whose
// flags are most often reached for while inspecting an already-armed marker.
var pausePointFlagOwnerSearchOrder = []string{
	pausePointEnableCommandName,
	clicore.PausePointAwaitCommandName,
	clicore.PausePointStatusUserCommandName,
}

// pausePointCarriedOverEnableFlagNames are the enable-pause-point flags whose values Unity reports
// back on every later status response (as Mode, MaxHistory, MaxPreviewElements and TimeoutSeconds).
// Passing one of these to a query command is not just misplaced, it is unnecessary — which is the
// part a caller cannot infer from "wrong command" alone.
var pausePointCarriedOverEnableFlagNames = []string{
	"mode",
	"max-history",
	"max-preview-elements",
	PausePointTimeoutFlagName,
}

// pausePointUnknownOptionError reports an unrecognized flag for a runner-owned native command.
// A flag that belongs to another pause-point command is reported as such, since naming the real
// owner (and, for enable-time settings, saying the value is already in this response) is what lets
// the caller recover without a second round trip. A flag that exists nowhere is reported as a plain
// unknown option: it cannot be a case of documentation running ahead of this build.
func pausePointUnknownOptionError(command string, name string) *clierrors.ArgumentError {
	message := fmt.Sprintf("Unknown option %q for %s.", "--"+name, command)
	if owner, ok := pausePointFlagOwnerCommand(name); ok && owner != command {
		message = fmt.Sprintf("--%s is %s %s option, not %s %s one.",
			name,
			indefiniteArticleFor(owner), owner,
			indefiniteArticleFor(command), command)
		if owner == pausePointEnableCommandName && isPausePointCarriedOverEnableFlag(name) {
			message += " The value passed to " + pausePointEnableCommandName +
				" is already applied to the response of this command, so it does not need to be passed again here."
		}
	}

	return &clierrors.ArgumentError{
		Message:     message,
		Option:      "--" + name,
		Command:     command,
		NextActions: []string{fmt.Sprintf("Run `uloop %s --help` to list the accepted options.", command)},
	}
}

// indefiniteArticleFor picks the article for a command name interpolated into a message. Command
// names are lower-case ASCII identifiers, so the initial letter decides it — "an await-pause-point
// option" rather than "a await-pause-point option". Both slots of the owner sentence go through
// this, so neither reads as broken English for a vowel-initial command.
func indefiniteArticleFor(commandName string) string {
	if commandName == "" {
		return "a"
	}
	if strings.ContainsRune("aeiou", rune(commandName[0])) {
		return "an"
	}
	return "a"
}

// pausePointFlagOwnerCommand reports which pause-point command accepts the flag, searching in a
// fixed order so the answer never depends on map iteration.
func pausePointFlagOwnerCommand(name string) (string, bool) {
	for _, command := range pausePointFlagOwnerSearchOrder {
		for _, flagName := range pausePointCommandFlagNames(command) {
			if flagName == name {
				return command, true
			}
		}
	}
	return "", false
}

// pausePointCommandFlagNames lists the flag names a pause-point command accepts, without the "--"
// prefix. Both sources are the same tables the commands' own --help output is built from, so a flag
// added to a command becomes recognizable here without a second registration.
func pausePointCommandFlagNames(command string) []string {
	if command == pausePointEnableCommandName {
		return pausePointEnableFlagNames()
	}

	names := make([]string, 0, len(runnerNativeCommandOptions[command]))
	for _, option := range runnerNativeCommandOptions[command] {
		names = append(names, strings.TrimPrefix(option, "--"))
	}
	return names
}

// pausePointEnableFlagNames lists enable-pause-point's CLI-only flags plus the ones derived from its
// Unity schema, since the misuse this message exists for (--max-preview-elements on a query command)
// is a schema-derived flag.
func pausePointEnableFlagNames() []string {
	names := make([]string, 0)
	for _, option := range tooldocs.PausePointEnableCLIOnlyOptions() {
		names = append(names, option.FlagName)
	}

	tool, ok := clicore.FindTool(clicore.LoadDefaultTools(), pausePointEnableCommandName)
	if !ok {
		return names
	}
	for propertyName, property := range tool.EffectiveInputSchema().Properties {
		names = append(names, tooldocs.OptionNameForProperty(tool.Name, propertyName, property))
	}
	return names
}

func isPausePointCarriedOverEnableFlag(name string) bool {
	for _, flagName := range pausePointCarriedOverEnableFlagNames {
		if flagName == name {
			return true
		}
	}
	return false
}
