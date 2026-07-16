// Command mockcli is the Windows stand-in for the POSIX shell mock scripts the
// automation tests install as fake `git`/`gh` executables on PATH. Why: Windows
// LookPath only resolves files with executable extensions, so an extensionless
// shell script is silently skipped and the real git/gh would run instead, and
// cmd batch files mangle arguments such as `tag^{commit}` or embedded quotes.
// The binary is built once by TestMain and copied next to a JSON config file
// (<name>.mockconfig.json) that selects which shell mock to emulate. Each mode
// below is a line-by-line translation of the corresponding script generator in
// the automation test files.
package main

import (
	"encoding/json"
	"fmt"
	"os"
	"path/filepath"
	"regexp"
	"strings"
	"time"
)

// mockCliConfig mirrors mockCliExecutableConfig in
// internal/automation/mock_cli_executable_for_tests_test.go.
type mockCliConfig struct {
	Mode        string                       `json:"mode"`
	RefResolves bool                         `json:"refResolves,omitempty"`
	Paths       map[string]mockCliPathConfig `json:"paths,omitempty"`
	PathOrder   []string                     `json:"pathOrder,omitempty"`
}

// mockCliPathConfig mirrors mockGitPathBehavior for the existence mode.
type mockCliPathConfig struct {
	Exists          bool   `json:"exists"`
	ShowOK          bool   `json:"showOK"`
	ShowContent     string `json:"showContent,omitempty"`
	ShowContentPath string `json:"showContentPath,omitempty"`
	ShowStderr      string `json:"showStderr,omitempty"`
	ProbeSleeps     bool   `json:"probeSleeps,omitempty"`
}

func main() {
	config, err := loadConfig()
	if err != nil {
		fmt.Fprintf(os.Stderr, "mockcli: %v\n", err)
		os.Exit(1)
	}

	args := os.Args[1:]
	switch config.Mode {
	case "existence":
		os.Exit(runExistenceGit(config, args))
	case "sleeping":
		// Mirrors writeSleepingMockGit: block long enough that a short context
		// timeout reliably kills the probe mid-run.
		time.Sleep(10 * time.Second)
		os.Exit(0)
	case "dispatcherMinimumVersionGit":
		os.Exit(runDispatcherMinimumVersionGit(args))
	case "protocolMinimumVersionGit":
		os.Exit(runProtocolMinimumVersionGit(args))
	case "protocolMinimumVersionGh":
		os.Exit(runProtocolMinimumVersionGh(args))
	default:
		fmt.Fprintf(os.Stderr, "mockcli: unknown mode %q\n", config.Mode)
		os.Exit(1)
	}
}

func loadConfig() (mockCliConfig, error) {
	executablePath, err := os.Executable()
	if err != nil {
		return mockCliConfig{}, fmt.Errorf("failed to locate executable: %w", err)
	}
	configPath := strings.TrimSuffix(executablePath, filepath.Ext(executablePath)) + ".mockconfig.json"
	content, err := os.ReadFile(configPath)
	if err != nil {
		return mockCliConfig{}, fmt.Errorf("failed to read config: %w", err)
	}
	config := mockCliConfig{}
	if err := json.Unmarshal(content, &config); err != nil {
		return mockCliConfig{}, fmt.Errorf("failed to decode config %s: %w", configPath, err)
	}
	return config, nil
}

// matchWildcard reports whether value matches a shell case label where `*`
// matches any run of characters (including separators).
func matchWildcard(pattern string, value string) bool {
	segments := strings.Split(pattern, "*")
	quoted := make([]string, 0, len(segments))
	for _, segment := range segments {
		quoted = append(quoted, regexp.QuoteMeta(segment))
	}
	matched, err := regexp.MatchString("^"+strings.Join(quoted, ".*")+"$", value)
	if err != nil {
		return false
	}
	return matched
}

func appendLogLine(logEnvName string, args []string) {
	logPath := os.Getenv(logEnvName)
	if logPath == "" {
		return
	}
	file, err := os.OpenFile(logPath, os.O_APPEND|os.O_CREATE|os.O_WRONLY, 0o644)
	if err != nil {
		fmt.Fprintf(os.Stderr, "mockcli: failed to open log: %v\n", err)
		os.Exit(1)
	}
	defer func() {
		_ = file.Close()
	}()
	if _, err := fmt.Fprintf(file, "%s\n", strings.Join(args, " ")); err != nil {
		fmt.Fprintf(os.Stderr, "mockcli: failed to append log: %v\n", err)
		os.Exit(1)
	}
}

func stripChdirFlag(args []string) []string {
	if len(args) >= 2 && args[0] == "-C" {
		return args[2:]
	}
	return args
}

func emitFileContent(path string) int {
	content, err := os.ReadFile(path)
	if err != nil {
		fmt.Fprintf(os.Stderr, "mockcli: %v\n", err)
		return 1
	}
	_, _ = os.Stdout.Write(content)
	return 0
}

func argAt(args []string, index int) string {
	if index < len(args) {
		return args[index]
	}
	return ""
}

// runExistenceGit mirrors buildExistenceMockGitScript in
// mock_git_for_tests_test.go.
func runExistenceGit(config mockCliConfig, args []string) int {
	args = stripChdirFlag(args)

	switch argAt(args, 0) {
	case "cat-file":
		target := argAt(args, 2)
		for _, key := range config.PathOrder {
			if !matchWildcard("*:"+key, target) {
				continue
			}
			behavior := config.Paths[key]
			if behavior.ProbeSleeps {
				time.Sleep(10 * time.Second)
				return 0
			}
			if behavior.Exists {
				return 0
			}
			return 1
		}
		fmt.Fprintf(os.Stderr, "unexpected cat-file target: %s\n", target)
		return 1
	case "rev-parse":
		if argAt(args, 1) == "--verify" {
			if config.RefResolves {
				return 0
			}
			return 1
		}
		fmt.Fprintf(os.Stderr, "unexpected rev-parse: %s\n", strings.Join(args, " "))
		return 1
	case "show":
		target := argAt(args, 1)
		for _, key := range config.PathOrder {
			if !matchWildcard("*:"+key, target) {
				continue
			}
			behavior := config.Paths[key]
			if behavior.ShowOK {
				if behavior.ShowContentPath != "" {
					return emitFileContent(behavior.ShowContentPath)
				}
				if behavior.ShowContent != "" {
					fmt.Fprintf(os.Stdout, "%s", behavior.ShowContent)
				}
				return 0
			}
			if behavior.ShowStderr != "" {
				fmt.Fprintf(os.Stderr, "%s\n", behavior.ShowStderr)
			}
			return 1
		}
		fmt.Fprintf(os.Stderr, "unexpected git show ref: %s\n", target)
		return 1
	default:
		fmt.Fprintf(os.Stderr, "unexpected git command: %s\n", strings.Join(args, " "))
		return 1
	}
}

// envConditionalShowRule emits the file named by ContentEnv when that variable
// is set, falls back to the file named by FallbackEnv when provided, and
// otherwise reports MissingStderr (with {target} replaced) and exits 1.
type envConditionalShowRule struct {
	pattern       string
	contentEnv    string
	fallbackEnv   string
	missingStderr string
}

type envConditionalCatFileRule struct {
	pattern string
	envName string
}

func runShowRules(rules []envConditionalShowRule, target string) int {
	for _, rule := range rules {
		if !matchWildcard(rule.pattern, target) {
			continue
		}
		contentPath := os.Getenv(rule.contentEnv)
		if contentPath == "" && rule.fallbackEnv != "" {
			contentPath = os.Getenv(rule.fallbackEnv)
		}
		if contentPath == "" {
			fmt.Fprintf(os.Stderr, "%s\n", strings.ReplaceAll(rule.missingStderr, "{target}", target))
			return 1
		}
		return emitFileContent(contentPath)
	}
	fmt.Fprintf(os.Stderr, "unexpected git show ref: %s\n", target)
	return 1
}

func runCatFileRules(rules []envConditionalCatFileRule, target string) int {
	for _, rule := range rules {
		if !matchWildcard(rule.pattern, target) {
			continue
		}
		if os.Getenv(rule.envName) != "" {
			return 0
		}
		return 1
	}
	fmt.Fprintf(os.Stderr, "unexpected cat-file target: %s\n", target)
	return 1
}

// runDispatcherMinimumVersionGit mirrors writeDispatcherMinimumVersionMockGit
// in dispatcher_minimum_version_guard_test.go.
func runDispatcherMinimumVersionGit(args []string) int {
	appendLogLine("GIT_LOG", args)

	if argAt(args, 0) == "rev-parse" && argAt(args, 1) == "--show-toplevel" {
		fmt.Fprintf(os.Stdout, "%s\n", os.Getenv("ULOOP_REPOSITORY_ROOT"))
		return 0
	}

	args = stripChdirFlag(args)

	showRules := []envConditionalShowRule{
		{
			pattern:       "dispatcher-v*:cli/dispatcher/dispatchercontract/dispatcher-contract.json",
			contentEnv:    "GIT_RELEASE_CONTRACT",
			missingStderr: "fatal: path 'cli/dispatcher/dispatchercontract/dispatcher-contract.json' exists on disk, but not in '{target}'",
		},
		{
			pattern:       "dispatcher-v*:cli/dispatcher/dispatcher-contract.json",
			contentEnv:    "GIT_PREVIOUS_RELEASE_CONTRACT",
			missingStderr: "previous release not found",
		},
		{
			pattern:       "dispatcher-v*:dispatcher/dispatcher-contract.json",
			contentEnv:    "GIT_MIDDLE_RELEASE_CONTRACT",
			missingStderr: "middle release not found",
		},
		{
			pattern:       "dispatcher-v*:cli/dispatcher-contract.json",
			contentEnv:    "GIT_LEGACY_RELEASE_CONTRACT",
			missingStderr: "release not found",
		},
	}
	catFileRules := []envConditionalCatFileRule{
		{pattern: "dispatcher-v*:cli/dispatcher/dispatchercontract/dispatcher-contract.json", envName: "GIT_RELEASE_CONTRACT"},
		{pattern: "dispatcher-v*:cli/dispatcher/dispatcher-contract.json", envName: "GIT_PREVIOUS_RELEASE_CONTRACT"},
		{pattern: "dispatcher-v*:dispatcher/dispatcher-contract.json", envName: "GIT_MIDDLE_RELEASE_CONTRACT"},
		{pattern: "dispatcher-v*:cli/dispatcher-contract.json", envName: "GIT_LEGACY_RELEASE_CONTRACT"},
	}

	switch argAt(args, 0) {
	case "show":
		return runShowRules(showRules, argAt(args, 1))
	case "cat-file":
		if argAt(args, 1) == "-e" {
			return runCatFileRules(catFileRules, argAt(args, 2))
		}
	case "rev-parse":
		if argAt(args, 1) == "--verify" {
			// Dispatcher release refs used in these tests are always considered
			// resolvable; the release publishing tests set up the associated
			// fixtures.
			return 0
		}
	}
	fmt.Fprintf(os.Stderr, "unexpected git command: %s\n", strings.Join(args, " "))
	return 1
}

// runProtocolMinimumVersionGit mirrors writeProtocolMinimumVersionMockGit in
// protocol_minimum_version_guard_test.go.
func runProtocolMinimumVersionGit(args []string) int {
	appendLogLine("GIT_LOG", args)

	if argAt(args, 0) == "rev-parse" && argAt(args, 1) == "--show-toplevel" {
		fmt.Fprintf(os.Stdout, "%s\n", os.Getenv("ULOOP_REPOSITORY_ROOT"))
		return 0
	}

	args = stripChdirFlag(args)

	showRules := []envConditionalShowRule{
		{pattern: "origin/v3-beta:Packages/src/project-runner-pin.json", contentEnv: "GIT_BASE_PIN"},
		{pattern: "origin/v3-beta:*", contentEnv: "GIT_BASE_CONSTANTS"},
		{
			pattern:     "protocol-pr-head:cli/common/clicontract/contract.json",
			contentEnv:  "GIT_HEAD_CONTRACT_CONTENT",
			fallbackEnv: "GIT_HEAD_CONSTANTS",
		},
		{pattern: "protocol-pr-head:Packages/src/project-runner-pin.json", contentEnv: "GIT_HEAD_PIN"},
		{pattern: "protocol-pr-head:*", contentEnv: "GIT_HEAD_CONSTANTS"},
		{pattern: "protocol-release:Packages/src/project-runner-pin.json", contentEnv: "GIT_HEAD_PIN"},
		{pattern: "protocol-release:*", contentEnv: "GIT_HEAD_CONSTANTS"},
		{
			pattern:       "uloop-project-runner-v*:cli/common/clicontract/contract.json",
			contentEnv:    "GIT_RELEASE_CONTENT",
			missingStderr: "fatal: path 'cli/common/clicontract/contract.json' exists on disk, but not in '{target}'",
		},
		{
			pattern:       "uloop-project-runner-v*:common/clicontract/contract.json",
			contentEnv:    "GIT_MIDDLE_RELEASE_CONTENT",
			missingStderr: "middle release not found",
		},
		{
			pattern:       "uloop-project-runner-v*:cli/contract.json",
			contentEnv:    "GIT_LEGACY_RELEASE_CONTENT",
			missingStderr: "release not found",
		},
	}
	catFileRules := []envConditionalCatFileRule{
		{pattern: "uloop-project-runner-v*:cli/common/clicontract/contract.json", envName: "GIT_RELEASE_CONTENT"},
		{pattern: "uloop-project-runner-v*:common/clicontract/contract.json", envName: "GIT_MIDDLE_RELEASE_CONTENT"},
		{pattern: "uloop-project-runner-v*:cli/contract.json", envName: "GIT_LEGACY_RELEASE_CONTENT"},
	}

	switch argAt(args, 0) {
	case "show":
		return runShowRules(showRules, argAt(args, 1))
	case "cat-file":
		if argAt(args, 1) == "-e" {
			return runCatFileRules(catFileRules, argAt(args, 2))
		}
	case "rev-parse":
		if argAt(args, 1) == "--verify" {
			// Release/base/head refs used in these tests are always resolvable;
			// the fallback flow only reaches rev-parse when a show has already
			// failed.
			return 0
		}
	}
	fmt.Fprintf(os.Stderr, "unexpected git command: %s\n", strings.Join(args, " "))
	return 1
}

// runProtocolMinimumVersionGh mirrors writeProtocolMinimumVersionMockGH in
// protocol_minimum_version_guard_test.go.
func runProtocolMinimumVersionGh(args []string) int {
	appendLogLine("GH_LOG", args)

	if argAt(args, 0) == "release" && argAt(args, 1) == "view" {
		releaseView := os.Getenv("GH_RELEASE_VIEW")
		if releaseView == "" {
			releaseView = `{"isDraft":false,"assets":[{"name":"uloop-project-runner-darwin-amd64.tar.gz","size":1},{"name":"uloop-project-runner-darwin-amd64.tar.gz.sha256","size":1},{"name":"uloop-project-runner-darwin-arm64.tar.gz","size":1},{"name":"uloop-project-runner-darwin-arm64.tar.gz.sha256","size":1},{"name":"uloop-project-runner-windows-amd64.zip","size":1},{"name":"uloop-project-runner-windows-amd64.zip.sha256","size":1}]}`
		}
		fmt.Fprintf(os.Stdout, "%s\n", releaseView)
		return 0
	}

	if argAt(args, 0) == "api" && argAt(args, 1) == "--paginate" {
		if commentIDs := os.Getenv("GH_COMMENT_IDS"); commentIDs != "" {
			fmt.Fprintf(os.Stdout, "%s\n", commentIDs)
		}
		return 0
	}

	if argAt(args, 0) == "api" && argAt(args, 1) == "--method" {
		return 0
	}

	fmt.Fprintf(os.Stderr, "unexpected gh command: %s\n", strings.Join(args, " "))
	return 1
}
