package cli

import (
	"io"
	"os"
	"path/filepath"
)

func runV3MigrationSkillInstall(projectRoot string, skills []skillDefinition, options skillCommandOptions, stdout io.Writer, stderr io.Writer) int {
	writeLine(stdout, "")
	writeFormat(stdout, "Installing V3 CLI invocation migration skill (%s)...\n", skillLocationName(options.global))
	writeLine(stdout, "")
	for _, target := range options.targets {
		result, err := installV3MigrationSkillForTarget(projectRoot, target, skills, options.global, groupManagedSkillsForOptions(options))
		if err != nil {
			writeClassifiedError(stderr, err, errorContext{projectRoot: projectRoot, command: skillsCommandName})
			return 1
		}
		writeFormat(stdout, "%s:\n", target.displayName)
		writeFormat(stdout, "  Installed: %d\n", result.installed)
		writeFormat(stdout, "  Updated: %d\n", result.updated)
		writeFormat(stdout, "  Skipped: %d\n", result.skipped)
		baseDir, err := getSkillsBaseDir(projectRoot, target, options.global)
		if err != nil {
			writeClassifiedError(stderr, err, errorContext{projectRoot: projectRoot, command: skillsCommandName})
			return 1
		}
		writeFormat(stdout, "  Location: %s\n\n", baseDir)
	}
	return 0
}

func runV3MigrationSkillUninstall(projectRoot string, options skillCommandOptions, stdout io.Writer, stderr io.Writer) int {
	writeLine(stdout, "")
	writeFormat(stdout, "Uninstalling V3 CLI invocation migration skill (%s)...\n", skillLocationName(options.global))
	writeLine(stdout, "")
	for _, target := range options.targets {
		grouped := groupManagedSkillsForOptions(options)
		removed, notFound, err := uninstallV3MigrationSkillForTarget(projectRoot, target, options.global, grouped)
		if err != nil {
			writeClassifiedError(stderr, err, errorContext{projectRoot: projectRoot, command: skillsCommandName})
			return 1
		}
		writeFormat(stdout, "%s:\n", target.displayName)
		writeFormat(stdout, "  Removed: %d\n", removed)
		writeFormat(stdout, "  Not found: %d\n", notFound)
		baseDir, err := getSkillsBaseDir(projectRoot, target, options.global)
		if err != nil {
			writeClassifiedError(stderr, err, errorContext{projectRoot: projectRoot, command: skillsCommandName})
			return 1
		}
		writeFormat(stdout, "  Location: %s\n\n", baseDir)
	}
	return 0
}

func installV3MigrationSkillForTarget(projectRoot string, target skillTarget, skills []skillDefinition, global bool, grouped bool) (skillInstallResult, error) {
	result := skillInstallResult{}
	baseDir, err := getSkillsBaseDir(projectRoot, target, global)
	if err != nil {
		return skillInstallResult{}, err
	}
	for _, skill := range skills {
		status, err := getSkillStatus(baseDir, skill, grouped)
		if err != nil {
			return skillInstallResult{}, err
		}
		destinationDir := getPreferredSkillDir(baseDir, skill.name, grouped)
		if status == "installed" {
			result.skipped++
			continue
		}
		if err := syncSkillDirectory(skill.sourceDirectory, destinationDir); err != nil {
			return skillInstallResult{}, err
		}
		alternateDir := getPreferredSkillDir(baseDir, skill.name, !grouped)
		if err := os.RemoveAll(alternateDir); err != nil {
			return skillInstallResult{}, err
		}
		if status == "outdated" {
			result.updated++
			continue
		}
		result.installed++
	}
	return result, nil
}

func uninstallV3MigrationSkillForTarget(projectRoot string, target skillTarget, global bool, grouped bool) (int, int, error) {
	removed := 0
	notFound := 0
	baseDir, err := getSkillsBaseDir(projectRoot, target, global)
	if err != nil {
		return removed, notFound, err
	}
	removedAny := false
	for _, layoutGrouped := range []bool{grouped, !grouped} {
		destinationDir := getPreferredSkillDir(baseDir, v3MigrationSkillName, layoutGrouped)
		exists, err := removeDirIfExists(destinationDir)
		if err != nil {
			return removed, notFound, err
		}
		if !exists {
			continue
		}
		removeEmptyMigrationSkillParent(baseDir, layoutGrouped)
		removedAny = true
	}

	if !removedAny {
		notFound++
		return removed, notFound, nil
	}
	removed++
	return removed, notFound, nil
}

func removeEmptyMigrationSkillParent(baseDir string, grouped bool) {
	if !grouped {
		return
	}
	_ = removeEmptyDir(filepath.Join(baseDir, managedSkillsDir))
}
