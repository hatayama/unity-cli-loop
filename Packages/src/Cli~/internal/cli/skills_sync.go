package cli

import (
	"bytes"
	"fmt"
	"os"
	"path/filepath"
)

func syncSkillDirectory(sourceDir string, destinationDir string) error {
	parentDir := filepath.Dir(destinationDir)
	if err := os.MkdirAll(parentDir, 0o755); err != nil {
		return err
	}
	tempDir, err := os.MkdirTemp(parentDir, filepath.Base(destinationDir)+".tmp-")
	if err != nil {
		return err
	}

	replaced := false
	defer func() {
		if !replaced {
			_ = os.RemoveAll(tempDir)
		}
	}()
	if err := copySkillDirectory(sourceDir, tempDir); err != nil {
		return err
	}
	if err := replaceSkillDirectory(tempDir, destinationDir); err != nil {
		return err
	}
	replaced = true
	return nil
}

func replaceSkillDirectory(sourceDir string, destinationDir string) error {
	if _, err := os.Stat(destinationDir); err != nil {
		if os.IsNotExist(err) {
			return os.Rename(sourceDir, destinationDir)
		}
		return err
	}

	parentDir := filepath.Dir(destinationDir)
	backupDir, err := os.MkdirTemp(parentDir, filepath.Base(destinationDir)+".backup-")
	if err != nil {
		return err
	}
	if err := os.Remove(backupDir); err != nil {
		return err
	}
	if err := os.Rename(destinationDir, backupDir); err != nil {
		return err
	}
	if err := os.Rename(sourceDir, destinationDir); err != nil {
		if restoreErr := os.Rename(backupDir, destinationDir); restoreErr != nil {
			return fmt.Errorf("replace skill directory failed: %w; restore failed: %v", err, restoreErr)
		}
		return err
	}
	return os.RemoveAll(backupDir)
}

func copySkillDirectory(sourceDir string, destinationDir string) error {
	return filepath.WalkDir(sourceDir, func(path string, entry os.DirEntry, walkErr error) error {
		if walkErr != nil {
			return walkErr
		}
		relativePath, err := filepath.Rel(sourceDir, path)
		if err != nil {
			return err
		}
		if relativePath == "." {
			return os.MkdirAll(destinationDir, 0o755)
		}
		if shouldSkipSkillFile(entry.Name()) {
			if entry.IsDir() {
				return filepath.SkipDir
			}
			return nil
		}

		destinationPath := filepath.Join(destinationDir, relativePath)
		if entry.IsDir() {
			return os.MkdirAll(destinationPath, 0o755)
		}

		content, err := os.ReadFile(path)
		if err != nil {
			return err
		}
		content = normalizeSkillFileContent(relativePath, content)
		return os.WriteFile(destinationPath, content, 0o644)
	})
}

func getSkillStatus(baseDir string, skill skillDefinition, grouped bool) (string, error) {
	return getSkillStatusWithStat(baseDir, skill, grouped, os.Stat)
}

func getSkillStatusWithStat(
	baseDir string,
	skill skillDefinition,
	grouped bool,
	stat func(string) (os.FileInfo, error),
) (string, error) {
	skillDir := getPreferredSkillDir(baseDir, skill.name, grouped)
	if _, err := stat(filepath.Join(skillDir, skillFileName)); err != nil {
		if os.IsNotExist(err) {
			return "not_installed", nil
		}
		return "", err
	}
	if isInstalledSkillOutdated(skillDir, skill) {
		return "outdated", nil
	}
	return "installed", nil
}

func isInstalledSkillOutdated(installedDir string, skill skillDefinition) bool {
	installedContent, err := os.ReadFile(filepath.Join(installedDir, skillFileName))
	if err != nil {
		return true
	}
	installedContent = normalizeSkillFileContent(skillFileName, installedContent)
	expectedContent := normalizeSkillFileContent(skillFileName, skill.content)
	if !bytes.Equal(installedContent, expectedContent) {
		return true
	}

	expectedFiles := collectComparableSkillFiles(skill.sourceDirectory)
	installedFiles := collectComparableSkillFiles(installedDir)
	if len(expectedFiles) != len(installedFiles) {
		return true
	}
	for relativePath, expectedContent := range expectedFiles {
		installedContent, ok := installedFiles[relativePath]
		if !ok || !bytes.Equal(expectedContent, installedContent) {
			return true
		}
	}
	return false
}

func collectComparableSkillFiles(root string) map[string][]byte {
	files := map[string][]byte{}
	_ = filepath.WalkDir(root, func(path string, entry os.DirEntry, walkErr error) error {
		if walkErr != nil || entry.IsDir() || shouldSkipSkillFile(entry.Name()) {
			return nil
		}
		relativePath, err := filepath.Rel(root, path)
		if err != nil || relativePath == skillFileName {
			return nil
		}
		content, err := os.ReadFile(path)
		if err != nil {
			return nil
		}
		files[relativePath] = normalizeSkillFileContent(relativePath, content)
		return nil
	})
	return files
}
