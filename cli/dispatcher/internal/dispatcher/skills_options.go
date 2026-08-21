package dispatcher

// Flag parsing and validation for `uloop skills` subcommands.

import (
	"strings"

	clierrors "github.com/hatayama/unity-cli-loop/common/errors"

	"github.com/hatayama/unity-cli-loop/common/clicore"
)

func parseSkillsOptions(args []string) (skillCommandOptions, error) {
	options := skillCommandOptions{}
	seenTargets := map[string]bool{}
	for index := 0; index < len(args); index++ {
		arg := args[index]
		switch {
		case arg == "-g" || arg == "--global":
			options.global = true
		case arg == "--flat":
			options.flat = true
		case arg == "--output-dir":
			// A flag-like next token (e.g. --global) must not be swallowed as the
			// destination, or the mutual-exclusion validation silently misses it.
			// Paths that genuinely start with a dash go through --output-dir=<path>.
			if index+1 >= len(args) || args[index+1] == "" || strings.HasPrefix(args[index+1], "-") {
				return skillCommandOptions{}, missingSkillsOutputDirValueError()
			}
			index++
			options.outputDir = args[index]
		case strings.HasPrefix(arg, "--output-dir="):
			options.outputDir = strings.TrimPrefix(arg, "--output-dir=")
			if options.outputDir == "" {
				return skillCommandOptions{}, missingSkillsOutputDirValueError()
			}
		default:
			if err := appendSkillTarget(&options, seenTargets, arg); err != nil {
				return skillCommandOptions{}, err
			}
		}
	}
	if err := validateSkillsOptions(options); err != nil {
		return skillCommandOptions{}, err
	}
	return options, nil
}

func appendSkillTarget(options *skillCommandOptions, seenTargets map[string]bool, arg string) error {
	targetID, ok := skillTargetIDFromFlag(arg)
	if !ok {
		return &clierrors.ArgumentError{
			Message:     "Unknown skills option: " + arg,
			Option:      arg,
			Command:     clicore.SkillsCommandName,
			NextActions: []string{"Run `uloop skills --help` to inspect supported skills options."},
		}
	}
	if seenTargets[targetID] {
		return nil
	}
	options.targets = append(options.targets, targetConfigs[targetID])
	seenTargets[targetID] = true
	return nil
}

func missingSkillsOutputDirValueError() error {
	return &clierrors.ArgumentError{
		Message: "The --output-dir option requires a directory path value.",
		Option:  "--output-dir",
		Command: clicore.SkillsCommandName,
		NextActions: []string{
			"Pass the destination directory, e.g. `uloop skills install --output-dir path/to/skills`.",
			"Use the --output-dir=<path> form when the destination path starts with a dash.",
		},
	}
}

// validateSkillsOptions rejects flag combinations whose destinations conflict:
// --output-dir already names one exact destination and always installs flat, so
// combining it with the location flags (--global or a target flag) or the layout
// flag (--flat) would make the request ambiguous.
func validateSkillsOptions(options skillCommandOptions) error {
	if options.outputDir == "" {
		return nil
	}
	conflicting := ""
	if options.flat {
		conflicting = "--flat"
	}
	if options.global {
		conflicting = "--global"
	}
	if len(options.targets) > 0 {
		conflicting = "--" + options.targets[0].id
	}
	if conflicting == "" {
		return nil
	}
	return &clierrors.ArgumentError{
		Message:     "The --output-dir option cannot be combined with " + conflicting + ".",
		Option:      "--output-dir",
		Command:     clicore.SkillsCommandName,
		NextActions: []string{"Use --output-dir alone, or drop it to install into target directories."},
	}
}

// skillTargetIDFromFlag reports the target id for a --<id> flag when it maps to
// a known entry in targetConfigs. The lookup is driven by targetConfigs so the
// set of accepted flags stays consistent with the help output.
func skillTargetIDFromFlag(arg string) (string, bool) {
	if !strings.HasPrefix(arg, "--") {
		return "", false
	}
	id := strings.TrimPrefix(arg, "--")
	if _, ok := targetConfigs[id]; !ok {
		return "", false
	}
	return id, true
}
