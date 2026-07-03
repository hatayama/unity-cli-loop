package main

import (
	"bytes"
	"context"
	"encoding/json"
	"errors"
	"flag"
	"fmt"
	"os"
	"os/exec"
	"path/filepath"
	"runtime"
	"strings"
	"time"
)

const (
	stateRelativePath         = "Temp/UnityCliLoop/server-state.json"
	expectedDynamicCodeResult = "cli-recovery-readiness-e2e"
	e2eDynamicCode            = `return "cli-recovery-readiness-e2e";`
	timeoutExitCode           = 124
)

type commandResult struct {
	args     []string
	exitCode int
	stdout   string
	stderr   string
	elapsed  time.Duration
	timedOut bool
}

type options struct {
	projectPath   string
	uloopPath     string
	timeout       time.Duration
	launchTimeout time.Duration
}

func main() {
	if err := run(); err != nil {
		fmt.Fprintf(os.Stderr, "ERROR: %v\n", err)
		os.Exit(1)
	}
}

func run() error {
	opts, err := parseOptions()
	if err != nil {
		return err
	}
	if err := validatePaths(opts.projectPath, opts.uloopPath); err != nil {
		return err
	}

	fmt.Println("=== CLI recovery/readiness smoke ===")
	fmt.Printf("project_path=%s\n", opts.projectPath)
	fmt.Printf("uloop_path=%s\n", opts.uloopPath)

	if err := runLiveRecoverySequence(opts); err != nil {
		return err
	}
	if err := runStaleRecoveryStateIgnoredSequence(opts.uloopPath, opts.timeout); err != nil {
		return err
	}

	fmt.Println("CLI recovery/readiness smoke passed")
	return nil
}

func parseOptions() (options, error) {
	defaultUloopPath, err := defaultUloopPath()
	if err != nil {
		return options{}, err
	}

	flagSet := flag.NewFlagSet(filepath.Base(os.Args[0]), flag.ContinueOnError)
	projectPath := flagSet.String("project-path", "", "Unity project to test")
	uloopPath := flagSet.String("uloop-path", defaultUloopPath, "uloop binary to execute")
	timeoutSeconds := flagSet.Float64("timeout", 120, "per-command timeout in seconds")
	launchTimeoutSeconds := flagSet.Float64("launch-timeout", 240, "launch timeout in seconds")
	if err := flagSet.Parse(os.Args[1:]); err != nil {
		return options{}, err
	}
	if strings.TrimSpace(*projectPath) == "" {
		return options{}, errors.New("--project-path is required")
	}

	resolvedProjectPath, err := filepath.Abs(*projectPath)
	if err != nil {
		return options{}, err
	}
	resolvedUloopPath := strings.TrimSpace(*uloopPath)
	if resolvedUloopPath != "" {
		resolvedUloopPath, err = filepath.Abs(resolvedUloopPath)
		if err != nil {
			return options{}, err
		}
	}

	return options{
		projectPath:   resolvedProjectPath,
		uloopPath:     resolvedUloopPath,
		timeout:       secondsToDuration(*timeoutSeconds),
		launchTimeout: secondsToDuration(*launchTimeoutSeconds),
	}, nil
}

func secondsToDuration(seconds float64) time.Duration {
	return time.Duration(seconds * float64(time.Second))
}

func defaultUloopPath() (string, error) {
	envPath := strings.TrimSpace(os.Getenv("ULOOP_BIN"))
	if envPath != "" {
		return envPath, nil
	}

	repoRoot, err := repoRootFromSource()
	if err != nil {
		return "", err
	}

	switch runtime.GOOS {
	case "darwin":
		arch := "darwin-amd64"
		if runtime.GOARCH == "arm64" {
			arch = "darwin-arm64"
		}
		return filepath.Join(repoRoot, "dist", arch, "uloop"), nil
	case "windows":
		return filepath.Join(repoRoot, "dist", "windows-amd64", "uloop.exe"), nil
	default:
		return "", nil
	}
}

func repoRootFromSource() (string, error) {
	_, sourceFile, _, ok := runtime.Caller(0)
	if !ok {
		return "", errors.New("could not locate smoke source file")
	}
	return filepath.Dir(filepath.Dir(sourceFile)), nil
}

func validatePaths(projectPath string, uloopPath string) error {
	if !isDirectory(filepath.Join(projectPath, "Assets")) {
		return fmt.Errorf("--project-path does not contain Assets: %s", projectPath)
	}
	if !isDirectory(filepath.Join(projectPath, "ProjectSettings")) {
		return fmt.Errorf("--project-path does not contain ProjectSettings: %s", projectPath)
	}
	if strings.TrimSpace(uloopPath) == "" {
		return errors.New("no built uloop binary is available for this platform. Pass --uloop-path")
	}
	if !isFile(uloopPath) {
		return fmt.Errorf("uloop binary not found: %s", uloopPath)
	}
	return nil
}

func isDirectory(path string) bool {
	info, err := os.Stat(path)
	return err == nil && info.IsDir()
}

func isFile(path string) bool {
	info, err := os.Stat(path)
	return err == nil && !info.IsDir()
}

func runLiveRecoverySequence(opts options) error {
	if err := assertSuccess(
		runUloop(opts.uloopPath, opts.projectPath, []string{"launch"}, opts.launchTimeout),
		"launch or reuse Unity",
	); err != nil {
		return err
	}
	if _, err := assertJSONResponse(
		runUloop(opts.uloopPath, opts.projectPath, []string{"get-logs", "--max-count", "1"}, opts.timeout),
		"initial get-logs readiness check",
	); err != nil {
		return err
	}
	if _, err := assertJSONSuccess(
		runUloop(opts.uloopPath, opts.projectPath, []string{"compile"}, opts.timeout),
		"compile with domain reload wait",
	); err != nil {
		return err
	}
	if _, err := assertJSONResponse(
		runUloop(opts.uloopPath, opts.projectPath, []string{"get-logs", "--max-count", "1"}, opts.timeout),
		"immediate get-logs after compile",
	); err != nil {
		return err
	}

	dynamicPayload, err := assertJSONSuccess(
		runUloop(
			opts.uloopPath,
			opts.projectPath,
			[]string{"execute-dynamic-code", "--code", e2eDynamicCode},
			opts.timeout,
		),
		"execute-dynamic-code after recovery",
	)
	if err != nil {
		return err
	}
	return assertDynamicCodeResult(dynamicPayload)
}

func runStaleRecoveryStateIgnoredSequence(uloopPath string, timeout time.Duration) error {
	projectPath, err := os.MkdirTemp("", "uloop-stale-state-")
	if err != nil {
		return err
	}
	defer os.RemoveAll(projectPath)

	if err := createMinimalUnityProject(projectPath); err != nil {
		return err
	}
	if err := writeStaleServerState(projectPath); err != nil {
		return err
	}

	fmt.Printf("stale_state_project=%s\n", projectPath)
	staleResult := runUloopWithEnv(
		uloopPath,
		projectPath,
		[]string{"get-logs", "--max-count", "1"},
		timeout,
		[]string{"ULOOP_FAKE_CONNECTION_FAILURE=1"},
	)
	if err := assertStaleRecoveryStateIgnored(staleResult); err != nil {
		return err
	}

	statePath := filepath.Join(projectPath, filepath.FromSlash(stateRelativePath))
	if !isFile(statePath) {
		return fmt.Errorf("stale recovery state should be ignored, not removed: %s", statePath)
	}
	return nil
}

func runUloop(uloopPath string, projectPath string, args []string, timeout time.Duration) commandResult {
	return runUloopWithEnv(uloopPath, projectPath, args, timeout, nil)
}

func runUloopWithEnv(uloopPath string, projectPath string, args []string, timeout time.Duration, extraEnv []string) commandResult {
	command := append([]string{uloopPath, "--project-path", projectPath}, args...)
	return runCommand(command, projectPath, timeout, extraEnv)
}

func runCommand(args []string, cwd string, timeout time.Duration, extraEnv []string) commandResult {
	startedAt := time.Now()
	ctx, cancel := context.WithTimeout(context.Background(), timeout)
	defer cancel()

	cmd := exec.CommandContext(ctx, args[0], args[1:]...)
	cmd.Dir = cwd
	if len(extraEnv) > 0 {
		cmd.Env = append(os.Environ(), extraEnv...)
	}
	var stdout bytes.Buffer
	var stderr bytes.Buffer
	cmd.Stdout = &stdout
	cmd.Stderr = &stderr

	err := cmd.Run()
	timedOut := ctx.Err() == context.DeadlineExceeded
	exitCode := exitCodeFromError(err, timedOut)

	return commandResult{
		args:     args,
		exitCode: exitCode,
		stdout:   stdout.String(),
		stderr:   stderr.String(),
		elapsed:  time.Since(startedAt),
		timedOut: timedOut,
	}
}

func exitCodeFromError(err error, timedOut bool) int {
	if timedOut {
		return timeoutExitCode
	}
	if err == nil {
		return 0
	}

	var exitError *exec.ExitError
	if errors.As(err, &exitError) {
		return exitError.ExitCode()
	}
	return 1
}

func assertSuccess(result commandResult, label string) error {
	if result.exitCode == 0 && !result.timedOut {
		fmt.Printf("%s passed in %.1fs\n", label, result.elapsed.Seconds())
		return nil
	}

	printCommandContext(label, result)
	return errors.New(label)
}

func assertJSONResponse(result commandResult, label string) (map[string]any, error) {
	if err := assertSuccess(result, label); err != nil {
		return nil, err
	}

	var payload map[string]any
	if err := json.Unmarshal([]byte(result.stdout), &payload); err != nil {
		printCommandContext(label, result)
		return nil, fmt.Errorf("%s did not return JSON: %w", label, err)
	}

	return payload, nil
}

func assertJSONSuccess(result commandResult, label string) (map[string]any, error) {
	payload, err := assertJSONResponse(result, label)
	if err != nil {
		return nil, err
	}

	if success, ok := payload["Success"].(bool); !ok || !success {
		printCommandContext(label, result)
		return nil, fmt.Errorf("%s returned invalid success payload: %v", label, payload)
	}

	return payload, nil
}

func assertDynamicCodeResult(payload map[string]any) error {
	result, ok := payload["Result"].(string)
	if ok && result == expectedDynamicCodeResult {
		return nil
	}
	return fmt.Errorf("execute-dynamic-code result mismatch: %v", payload)
}

func assertStaleRecoveryStateIgnored(result commandResult) error {
	if result.exitCode == 0 || result.timedOut {
		printCommandContext("stale recovery-state ignored", result)
		return errors.New("stale recovery-state ignored check should fail without timing out")
	}

	combinedOutput := result.stdout + result.stderr
	requiredFragments := []string{
		"UNITY_NOT_REACHABLE",
		"Unity CLI Loop server is not reachable",
	}
	for _, fragment := range requiredFragments {
		if !strings.Contains(combinedOutput, fragment) {
			printCommandContext("stale recovery-state ignored", result)
			return fmt.Errorf("stale recovery-state ignored output missing: %s", fragment)
		}
	}
	forbiddenFragments := []string{
		"uloop " + "fix",
		"stale Unity CLI Loop recovery state",
	}
	for _, fragment := range forbiddenFragments {
		if strings.Contains(combinedOutput, fragment) {
			printCommandContext("stale recovery-state ignored", result)
			return fmt.Errorf("stale recovery-state ignored output should not contain: %s", fragment)
		}
	}

	fmt.Printf("stale recovery-state ignored in %.1fs\n", result.elapsed.Seconds())
	return nil
}

func printCommandContext(label string, result commandResult) {
	fmt.Printf("%s failed\n", label)
	fmt.Printf("command: %s\n", strings.Join(result.args, " "))
	fmt.Printf("exit_code: %d\n", result.exitCode)
	fmt.Printf("elapsed: %.1fs\n", result.elapsed.Seconds())
	fmt.Printf("timed_out: %t\n", result.timedOut)
	fmt.Println("--- stdout ---")
	fmt.Print(result.stdout)
	fmt.Println("--- stderr ---")
	fmt.Print(result.stderr)
}

func createMinimalUnityProject(projectPath string) error {
	if err := os.MkdirAll(filepath.Join(projectPath, "Assets"), 0o755); err != nil {
		return err
	}
	projectSettingsPath := filepath.Join(projectPath, "ProjectSettings")
	if err := os.MkdirAll(projectSettingsPath, 0o755); err != nil {
		return err
	}
	return os.WriteFile(
		filepath.Join(projectSettingsPath, "ProjectVersion.txt"),
		[]byte("m_EditorVersion: 6000.0.0f1\n"),
		0o644,
	)
}

func writeStaleServerState(projectPath string) error {
	statePath := filepath.Join(projectPath, filepath.FromSlash(stateRelativePath))
	if err := os.MkdirAll(filepath.Dir(statePath), 0o755); err != nil {
		return err
	}

	state := map[string]string{
		"phase":        "recovering",
		"generationId": "stale-e2e",
		"updatedAt":    "1970-01-01T00:00:00Z",
		"reason":       "domain-reload-after",
		"endpoint":     "stale-e2e",
		"lastError":    "",
	}
	data, err := json.Marshal(state)
	if err != nil {
		return err
	}
	return os.WriteFile(statePath, data, 0o644)
}
