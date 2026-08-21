package dispatcher

import (
	"io"
	"path/filepath"

	clierrors "github.com/hatayama/unity-cli-loop/common/errors"

	"github.com/hatayama/unity-cli-loop/common/clicore"
)

func runV3MigrationSkillsSubcommand(
	subcommand string,
	projectRoot string,
	options skillCommandOptions,
	stdout io.Writer,
	stderr io.Writer,
) int {
	if len(options.targets) == 0 {
		printSkillsTargetGuidance(subcommand, stdout)
		return 0
	}

	switch subcommand {
	case "install-v3-migration":
		skills, err := collectV3MigrationSkillDefinition(projectRoot)
		if err != nil {
			clierrors.WriteClassifiedError(stderr, err, clierrors.ErrorContext{ProjectRoot: projectRoot, Command: clicore.SkillsCommandName})
			return 1
		}
		return runV3MigrationSkillInstall(projectRoot, skills, options, stdout, stderr)
	case "uninstall-v3-migration":
		return runV3MigrationSkillUninstall(projectRoot, options, stdout, stderr)
	default:
		return 1
	}
}

func runSkillsSubcommand(
	subcommand string,
	projectRoot string,
	skills []skillDefinition,
	options skillCommandOptions,
	stdout io.Writer,
	stderr io.Writer,
) int {
	if options.outputDir != "" {
		return runSkillsDirSubcommand(subcommand, skills, options.outputDir, stdout, stderr)
	}
	switch subcommand {
	case "list":
		return runSkillsList(projectRoot, skills, options, stdout, stderr)
	case "install":
		return runSkillsInstallWithGuidance(projectRoot, skills, options, stdout, stderr)
	case "uninstall":
		return runSkillsUninstallWithGuidance(projectRoot, skills, options, stdout, stderr)
	default:
		return 1
	}
}

func runSkillsDirSubcommand(
	subcommand string,
	skills []skillDefinition,
	directory string,
	stdout io.Writer,
	stderr io.Writer,
) int {
	// Resolved once here so the three subcommand runners share one absolute
	// path and one failure path instead of each repeating the resolution.
	absDir, err := filepath.Abs(directory)
	if err != nil {
		clierrors.WriteClassifiedError(stderr, err, skillsDirErrorContext())
		return 1
	}
	switch subcommand {
	case "list":
		return runSkillsDirList(absDir, skills, stdout, stderr)
	case "install":
		return runSkillsDirInstall(absDir, skills, stdout, stderr)
	case "uninstall":
		return runSkillsDirUninstall(absDir, skills, stdout, stderr)
	default:
		// Routing already rejects unknown and v3-migration subcommands, so this
		// only fires when a new subcommand is added without dir-mode support.
		clierrors.WriteClassifiedError(stderr, &clierrors.ArgumentError{
			Message:     "The " + subcommand + " subcommand does not support --output-dir.",
			Option:      "--output-dir",
			Command:     clicore.SkillsCommandName,
			NextActions: []string{"Use --output-dir with the install, uninstall, or list subcommand."},
		}, clierrors.ErrorContext{Command: clicore.SkillsCommandName})
		return 1
	}
}

func runSkillsInstallWithGuidance(
	projectRoot string,
	skills []skillDefinition,
	options skillCommandOptions,
	stdout io.Writer,
	stderr io.Writer,
) int {
	if len(options.targets) == 0 {
		printSkillsTargetGuidance("install", stdout)
		return 0
	}
	return runSkillsInstall(projectRoot, skills, options, stdout, stderr)
}

func runSkillsUninstallWithGuidance(
	projectRoot string,
	skills []skillDefinition,
	options skillCommandOptions,
	stdout io.Writer,
	stderr io.Writer,
) int {
	if len(options.targets) == 0 {
		printSkillsTargetGuidance("uninstall", stdout)
		return 0
	}
	return runSkillsUninstall(projectRoot, skills, options, stdout, stderr)
}
