package dispatcher

import (
	"fmt"
	"io"
	"os"
	"path/filepath"
	"runtime"
	"strings"

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
	if err := posixStyleOutputDirError(runtime.GOOS, directory); err != nil {
		clierrors.WriteClassifiedError(stderr, err, skillsDirErrorContext())
		return 1
	}
	// Resolved once here so the three subcommand runners share one absolute
	// path and one failure path instead of each repeating the resolution.
	absDir, err := filepath.Abs(directory)
	if err != nil {
		clierrors.WriteClassifiedError(stderr, err, skillsDirErrorContext())
		return 1
	}
	// The destination must be a directory or absent (install creates it).
	// Checked up front because a file here would otherwise surface as a raw
	// ENOTDIR on Unix but as bogus not-installed statuses on Windows.
	info, err := os.Stat(absDir)
	if err != nil && !os.IsNotExist(err) {
		clierrors.WriteClassifiedError(stderr, err, skillsDirErrorContext())
		return 1
	}
	if err == nil && !info.IsDir() {
		clierrors.WriteClassifiedError(stderr, fmt.Errorf("the %s path %s exists but is not a directory", skillsOutputDirFlagName, absDir), skillsDirErrorContext())
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
			Message:     "The " + subcommand + " subcommand does not support " + skillsOutputDirFlagName + ".",
			Option:      skillsOutputDirFlagName,
			Command:     clicore.SkillsCommandName,
			NextActions: []string{"Use " + skillsOutputDirFlagName + " with the install, uninstall, or list subcommand."},
		}, skillsDirErrorContext())
		return 1
	}
}

// posixStyleOutputDirError rejects a POSIX-style absolute path on Windows
// (Git Bash's /c/apm or /tmp/skills). Such a path has no volume name, so
// filepath.Abs would silently anchor it under the current drive and the sync
// would write to an unintended directory. Rejected rather than normalized:
// the dispatcher cannot know which drive the shell meant. The goos parameter
// exists so the rule is testable on every platform.
func posixStyleOutputDirError(goos string, directory string) error {
	if goos != "windows" || !strings.HasPrefix(directory, "/") {
		return nil
	}
	return &clierrors.ArgumentError{
		Message:     "The " + skillsOutputDirFlagName + " path " + directory + " is a POSIX-style path, which is ambiguous on Windows.",
		Option:      skillsOutputDirFlagName,
		Command:     clicore.SkillsCommandName,
		NextActions: []string{"Pass a Windows path such as C:\\path\\to\\skills."},
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
