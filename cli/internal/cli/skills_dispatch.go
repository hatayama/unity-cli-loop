package cli

import "io"

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
			writeClassifiedError(stderr, err, errorContext{projectRoot: projectRoot, command: skillsCommandName})
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
