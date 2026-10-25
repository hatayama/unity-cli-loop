package dispatcher

// The install root is the directory whose agent directories (.claude/skills,
// .agents/skills, ...) receive project-local skills. Teams split on where
// their agents start: some open the Unity project itself, others open the
// Git repository that contains it, so both are supported. The choice made in
// the Editor is saved in the project's .uloop/settings.skills.json, and a
// flagless `uloop skills` command follows it so the CLI and the Editor never
// install into different places; --root overrides it for one invocation.
// Only the destination moves: skill sources and tool settings still come from
// the Unity project root.

import (
	"encoding/json"
	"fmt"
	"os"
	"path/filepath"
	"strings"

	clierrors "github.com/hatayama/unity-cli-loop/common/errors"

	"github.com/hatayama/unity-cli-loop/common/clicore"
)

type skillsInstallRoot string

const (
	skillsInstallRootUnity skillsInstallRoot = "unity"
	skillsInstallRootGit   skillsInstallRoot = "git"

	// skillsRootFlagName is the single definition of the --root token, shared
	// by parsing, validation, errors, and help output.
	skillsRootFlagName = "--root"

	skillSettingsDir  = ".uloop"
	skillSettingsFile = "settings.skills.json"
	gitMarkerName     = ".git"
)

type skillSettingsData struct {
	InstallRoot string `json:"installRoot"`
}

func parseSkillsInstallRoot(value string) (skillsInstallRoot, bool) {
	switch skillsInstallRoot(value) {
	case skillsInstallRootUnity, skillsInstallRootGit:
		return skillsInstallRoot(value), true
	default:
		return "", false
	}
}

// parseRootOption consumes the --root option at args[index] in either value
// form and returns the index of the last consumed token.
func parseRootOption(options *skillCommandOptions, args []string, index int) (int, error) {
	if options.root != "" {
		return index, &clierrors.ArgumentError{
			Message:     "Duplicate skills option: " + skillsRootFlagName,
			Option:      skillsRootFlagName,
			Command:     clicore.SkillsCommandName,
			NextActions: []string{"Pass the install root only once."},
		}
	}
	value, joined := strings.CutPrefix(args[index], skillsRootFlagName+"=")
	nextIndex := index
	if !joined {
		// A flag-like next token must not be swallowed as the value, or
		// `--root --claude` would silently drop the target.
		if index+1 >= len(args) || strings.HasPrefix(args[index+1], "-") {
			return index, invalidSkillsRootValueError("")
		}
		value = args[index+1]
		nextIndex = index + 1
	}
	root, ok := parseSkillsInstallRoot(value)
	if !ok {
		return index, invalidSkillsRootValueError(value)
	}
	options.root = root
	return nextIndex, nil
}

func invalidSkillsRootValueError(received string) error {
	return &clierrors.ArgumentError{
		Message:     "The " + skillsRootFlagName + " option requires unity or git.",
		Option:      skillsRootFlagName,
		Received:    received,
		Command:     clicore.SkillsCommandName,
		NextActions: []string{"Pass `--root unity` for the Unity project root or `--root git` for the Git repository root."},
	}
}

// loadSavedSkillsInstallRoot returns the install root saved by the Editor, or
// unity when none is saved. An unreadable or unknown value is an error rather
// than a silent fallback: installing at the Unity root when the team chose the
// Git root would leave agents reading stale skills with no visible failure.
func loadSavedSkillsInstallRoot(projectRoot string) (skillsInstallRoot, error) {
	settingsPath := filepath.Join(projectRoot, skillSettingsDir, skillSettingsFile)
	content, err := os.ReadFile(settingsPath)
	if os.IsNotExist(err) {
		return skillsInstallRootUnity, nil
	}
	if err != nil {
		return "", err
	}
	if strings.TrimSpace(string(content)) == "" {
		return skillsInstallRootUnity, nil
	}
	settings := skillSettingsData{}
	if err := json.Unmarshal(content, &settings); err != nil {
		return "", fmt.Errorf("failed to parse %s: %w", settingsPath, err)
	}
	if settings.InstallRoot == "" {
		return skillsInstallRootUnity, nil
	}
	root, ok := parseSkillsInstallRoot(settings.InstallRoot)
	if !ok {
		return "", fmt.Errorf("%s has an unknown installRoot %q; expected unity or git", settingsPath, settings.InstallRoot)
	}
	return root, nil
}

// findGitRepositoryRoot walks up from the Unity project root, inclusive, to the
// nearest directory holding .git. A .git file counts too, because worktree and
// submodule checkouts mark their root with one.
func findGitRepositoryRoot(projectRoot string) (string, error) {
	currentPath, err := filepath.Abs(projectRoot)
	if err != nil {
		return "", err
	}
	for {
		if _, err := os.Lstat(filepath.Join(currentPath, gitMarkerName)); err == nil {
			return currentPath, nil
		} else if !os.IsNotExist(err) {
			return "", err
		}
		parentPath := filepath.Dir(currentPath)
		if parentPath == currentPath {
			return "", gitRepositoryNotFoundError(projectRoot)
		}
		currentPath = parentPath
	}
}

func gitRepositoryNotFoundError(projectRoot string) error {
	return &clierrors.ArgumentError{
		Message:     "No Git repository contains the Unity project " + projectRoot + ", so skills cannot be installed at the Git root.",
		Option:      skillsRootFlagName,
		Command:     clicore.SkillsCommandName,
		NextActions: []string{"Pass `--root unity` to install at the Unity project root, or run the command inside a Git repository."},
	}
}

// resolveSkillsInstallLocation fills in the effective install root and its
// directory. Global installs (under the home directory) and --output-dir syncs
// already name their destination, so they neither read the saved setting nor
// resolve a project directory.
func resolveSkillsInstallLocation(projectRoot string, options skillCommandOptions) (skillCommandOptions, error) {
	if options.global || options.outputDir != "" {
		return options, nil
	}
	root := options.root
	if root == "" {
		savedRoot, err := loadSavedSkillsInstallRoot(projectRoot)
		if err != nil {
			return skillCommandOptions{}, err
		}
		root = savedRoot
	}
	resolved := options
	resolved.root = root
	if root == skillsInstallRootUnity {
		resolved.installRoot = projectRoot
		return resolved, nil
	}
	gitRoot, err := findGitRepositoryRoot(projectRoot)
	if err != nil {
		return skillCommandOptions{}, err
	}
	resolved.installRoot = gitRoot
	return resolved, nil
}

// skillsInstallRootFor returns the directory that receives project-local agent
// directories. An unresolved option set means the Unity project root, the
// default install root.
func skillsInstallRootFor(projectRoot string, options skillCommandOptions) string {
	if options.installRoot == "" {
		return projectRoot
	}
	return options.installRoot
}

// skillLocationName names the install location in progress messages.
func skillLocationName(options skillCommandOptions) string {
	return strings.ToLower(skillLocationLabel(options))
}

// skillLocationLabel names the install location in list headings.
func skillLocationLabel(options skillCommandOptions) string {
	if options.global {
		return "Global"
	}
	if options.root == skillsInstallRootGit {
		return "Git root"
	}
	return "Project"
}
