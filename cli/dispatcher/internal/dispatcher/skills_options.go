package dispatcher

// Flag parsing and validation for `uloop skills` subcommands.

import (
	"strings"

	clierrors "github.com/hatayama/unity-cli-loop/common/errors"

	"github.com/hatayama/unity-cli-loop/common/clicore"
)

// skillsOutputDirFlagName is the single definition of the --output-dir token.
// Parsing, error construction, routing, and the help option line all reference
// it so a rename cannot silently miss one of the comparison sites.
const skillsOutputDirFlagName = "--output-dir"

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
		case arg == skillsOutputDirFlagName || strings.HasPrefix(arg, skillsOutputDirFlagName+"="):
			nextIndex, err := parseOutputDirOption(&options, args, index)
			if err != nil {
				return skillCommandOptions{}, err
			}
			index = nextIndex
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

// parseOutputDirOption consumes the --output-dir option at args[index] in
// either value form and returns the index of the last consumed token.
func parseOutputDirOption(options *skillCommandOptions, args []string, index int) (int, error) {
	// Rejected rather than letting the last occurrence win, matching
	// `uloop install --dir`: two destinations in one invocation is
	// ambiguous, and silently syncing only one of them would hide it.
	if options.outputDir != "" {
		return index, duplicateSkillsOutputDirError()
	}
	if value, found := strings.CutPrefix(args[index], skillsOutputDirFlagName+"="); found {
		if strings.TrimSpace(value) == "" {
			return index, missingSkillsOutputDirValueError()
		}
		options.outputDir = value
		return index, nil
	}
	// A flag-like next token (e.g. --global) must not be swallowed as the
	// destination, or the mutual-exclusion validation silently misses it, and a
	// whitespace-only token (an unset shell variable) must not become a
	// directory literally named after it. Paths that genuinely start with a
	// dash go through --output-dir=<path>.
	if index+1 >= len(args) || strings.TrimSpace(args[index+1]) == "" || strings.HasPrefix(args[index+1], "-") {
		return index, missingSkillsOutputDirValueError()
	}
	options.outputDir = args[index+1]
	return index + 1, nil
}

func duplicateSkillsOutputDirError() error {
	return &clierrors.ArgumentError{
		Message:     "Duplicate skills option: " + skillsOutputDirFlagName,
		Option:      skillsOutputDirFlagName,
		Command:     clicore.SkillsCommandName,
		NextActions: []string{"Pass the destination directory only once."},
	}
}

func missingSkillsOutputDirValueError() error {
	return &clierrors.ArgumentError{
		Message: "The " + skillsOutputDirFlagName + " option requires a directory path value.",
		Option:  skillsOutputDirFlagName,
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
	if len(options.targets) > 0 {
		return skillsOutputDirConflictError("--" + options.targets[0].id)
	}
	if options.global {
		return skillsOutputDirConflictError("--global")
	}
	if options.flat {
		return skillsOutputDirConflictError("--flat")
	}
	return nil
}

func skillsOutputDirConflictError(conflicting string) error {
	return &clierrors.ArgumentError{
		Message:     "The " + skillsOutputDirFlagName + " option cannot be combined with " + conflicting + ".",
		Option:      skillsOutputDirFlagName,
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
