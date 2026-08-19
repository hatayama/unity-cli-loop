package dispatcher

import (
	"bytes"
	"context"
	"encoding/json"
	"io"
	"os"
	"path/filepath"

	clierrors "github.com/hatayama/unity-cli-loop/common/errors"

	"github.com/hatayama/unity-cli-loop/common/clicore"
	"github.com/hatayama/unity-cli-loop/common/project"
)

const packageVersionFlagName = "version"

type packageInstallOptions struct {
	version string
}

func tryHandlePackageRequest(
	ctx context.Context,
	args []string,
	startPath string,
	globalProjectPath string,
	stdout io.Writer,
	stderr io.Writer,
) (bool, int) {
	if len(args) == 0 || args[0] != clicore.PackageCommandName {
		return false, 0
	}
	if len(args) == 1 || clicore.IsHelpRequest(args[1:]) {
		printPackageHelp(stdout)
		return true, 0
	}

	subcommand := args[1]
	if !isKnownPackageSubcommand(subcommand) {
		clierrors.WriteErrorEnvelope(stderr, unknownPackageSubcommandError(subcommand, clierrors.ErrorContext{Command: clicore.PackageCommandName}))
		return true, 1
	}
	if clicore.ContainsHelpRequest(args[2:]) {
		printPackageHelp(stdout)
		return true, 0
	}

	switch subcommand {
	case "install":
		return true, runPackageInstall(ctx, args[2:], startPath, globalProjectPath, stdout, stderr)
	case "status":
		return true, runPackageStatus(args[2:], startPath, globalProjectPath, stdout, stderr)
	default:
		return true, 1
	}
}

func isKnownPackageSubcommand(subcommand string) bool {
	switch subcommand {
	case "install", "status":
		return true
	default:
		return false
	}
}

func unknownPackageSubcommandError(subcommand string, context clierrors.ErrorContext) clierrors.CLIError {
	return (&clierrors.ArgumentError{
		Message:     "Unknown package command: " + subcommand,
		Received:    subcommand,
		Command:     clicore.PackageCommandName,
		NextActions: []string{"Use `uloop package install` or `uloop package status`."},
	}).ToCLIError(context)
}

func parsePackageInstallOptions(args []string) (packageInstallOptions, error) {
	options := packageInstallOptions{}
	for index := 0; index < len(args); index++ {
		arg := args[index]
		name, value, consumedNext, err := clicore.ParseFlagValue(arg, args, index)
		if err != nil {
			return packageInstallOptions{}, err
		}
		if name != packageVersionFlagName {
			return packageInstallOptions{}, &clierrors.ArgumentError{
				Message:     "Unknown package install option: --" + name,
				Option:      "--" + name,
				Command:     clicore.PackageCommandName,
				NextActions: []string{"Run `uloop package install --help` to inspect supported options."},
			}
		}
		if value == "" {
			return packageInstallOptions{}, &clierrors.ArgumentError{
				Message:     "Empty value for --version",
				Option:      "--version",
				Command:     clicore.PackageCommandName,
				NextActions: []string{"Pass a non-empty package version with `--version <x.y.z>`."},
			}
		}
		if options.version != "" {
			return packageInstallOptions{}, &clierrors.ArgumentError{
				Message:     "Duplicate package install option: --version",
				Option:      "--version",
				Command:     clicore.PackageCommandName,
				NextActions: []string{"Pass --version only once."},
			}
		}
		options.version = value
		if consumedNext {
			index++
		}
	}
	return options, nil
}

func parsePackageStatusOptions(args []string) error {
	for _, arg := range args {
		return &clierrors.ArgumentError{
			Message:     "Unknown package status option: " + arg,
			Option:      arg,
			Command:     clicore.PackageCommandName,
			NextActions: []string{"Run `uloop package status` with no options, or pass `--project-path` as a global flag."},
		}
	}
	return nil
}

func resolvePackageProjectRoot(startPath string, explicitProjectPath string) (string, error) {
	if explicitProjectPath != "" {
		return project.ResolveExplicitProjectRoot(explicitProjectPath)
	}
	return project.FindUnityProjectRoot(startPath)
}

func runPackageInstall(
	ctx context.Context,
	args []string,
	startPath string,
	explicitProjectPath string,
	stdout io.Writer,
	stderr io.Writer,
) int {
	options, err := parsePackageInstallOptions(args)
	if err != nil {
		clierrors.WriteClassifiedError(stderr, err, clierrors.ErrorContext{Command: clicore.PackageCommandName})
		return 1
	}

	projectRoot, err := resolvePackageProjectRoot(startPath, explicitProjectPath)
	if err != nil {
		clierrors.WriteClassifiedError(stderr, err, clierrors.ErrorContext{Command: clicore.PackageCommandName})
		return 1
	}

	manifestPath := filepath.Join(projectRoot, filepath.FromSlash(dispatcherPackagesManifestRelativePath))
	content, err := os.ReadFile(manifestPath)
	if err != nil {
		clierrors.WriteErrorEnvelope(stderr, packageManifestInvalidError(projectRoot, err))
		return 1
	}

	version := options.version
	if version == "" {
		version, err = resolveLatestPackageVersion(ctx)
		if err != nil {
			clierrors.WriteErrorEnvelope(stderr, packageRegistryUnavailableError(projectRoot, err))
			return 1
		}
	}

	result, err := mergePackageManifest(content, version)
	if err != nil {
		clierrors.WriteErrorEnvelope(stderr, packageManifestInvalidError(projectRoot, err))
		return 1
	}

	if !result.Changed {
		clicore.WriteFormat(stdout, "%s %s is already installed. Nothing to do.\n", dispatcherUnityPackageName, version)
		return 0
	}

	if err := writePackageManifestAtomically(manifestPath, result.Content); err != nil {
		clierrors.WriteClassifiedError(stderr, err, clierrors.ErrorContext{ProjectRoot: projectRoot, Command: clicore.PackageCommandName})
		return 1
	}

	writePackageInstallSuccess(stdout, result, version)
	return 0
}

func runPackageStatus(
	args []string,
	startPath string,
	explicitProjectPath string,
	stdout io.Writer,
	stderr io.Writer,
) int {
	if err := parsePackageStatusOptions(args); err != nil {
		clierrors.WriteClassifiedError(stderr, err, clierrors.ErrorContext{Command: clicore.PackageCommandName})
		return 1
	}

	projectRoot, err := resolvePackageProjectRoot(startPath, explicitProjectPath)
	if err != nil {
		clierrors.WriteClassifiedError(stderr, err, clierrors.ErrorContext{Command: clicore.PackageCommandName})
		return 1
	}

	manifestPath := filepath.Join(projectRoot, filepath.FromSlash(dispatcherPackagesManifestRelativePath))
	content, err := os.ReadFile(manifestPath)
	if err != nil {
		clierrors.WriteErrorEnvelope(stderr, packageManifestInvalidError(projectRoot, err))
		return 1
	}

	status, err := inspectPackageManifestStatus(content)
	if err != nil {
		clierrors.WriteErrorEnvelope(stderr, packageManifestInvalidError(projectRoot, err))
		return 1
	}

	clicore.WriteFormat(stdout, "Package: %s\n", dispatcherUnityPackageName)
	if status.registryInstalled {
		clicore.WriteFormat(stdout, "Scoped registry: installed (%s)\n", openUPMRegistryURL)
	} else {
		clicore.WriteLine(stdout, "Scoped registry: not installed")
	}
	if status.dependencyInstalled {
		clicore.WriteFormat(stdout, "Dependency: installed (%s)\n", status.dependencyVersion)
	} else {
		clicore.WriteLine(stdout, "Dependency: not installed")
	}
	return 0
}

type packageManifestStatus struct {
	registryInstalled   bool
	dependencyInstalled bool
	dependencyVersion   string
}

func inspectPackageManifestStatus(content []byte) (packageManifestStatus, error) {
	normalized := bytesReplaceCRLF(content)
	root, err := parseOrderedJSONObjectBytes(normalized)
	if err != nil {
		return packageManifestStatus{}, err
	}

	status := packageManifestStatus{}
	if inspectErr := inspectPackageManifestDependency(root, &status); inspectErr != nil {
		return packageManifestStatus{}, inspectErr
	}
	if inspectErr := inspectPackageManifestOpenUPMRegistry(root, &status); inspectErr != nil {
		return packageManifestStatus{}, inspectErr
	}
	return status, nil
}

func inspectPackageManifestDependency(root orderedJSONObject, status *packageManifestStatus) error {
	rawDependencies, ok := root.values["dependencies"]
	if !ok {
		return nil
	}
	dependencies, depErr := parseOrderedJSONObjectBytes(rawDependencies)
	if depErr != nil {
		return depErr
	}
	rawVersion, found := dependencies.values[dispatcherUnityPackageName]
	if !found {
		return nil
	}
	version := ""
	if unmarshalErr := json.Unmarshal(rawVersion, &version); unmarshalErr != nil {
		return unmarshalErr
	}
	status.dependencyInstalled = true
	status.dependencyVersion = version
	return nil
}

func inspectPackageManifestOpenUPMRegistry(root orderedJSONObject, status *packageManifestStatus) error {
	rawRegistries, ok := root.values["scopedRegistries"]
	if !ok {
		return nil
	}
	elements, arrayErr := parseJSONRawArray(rawRegistries)
	if arrayErr != nil {
		return arrayErr
	}
	installed, err := openUPMRegistryHasPackageScope(elements)
	if err != nil {
		return err
	}
	status.registryInstalled = installed
	return nil
}

func openUPMRegistryHasPackageScope(elements []json.RawMessage) (bool, error) {
	for _, element := range elements {
		entry, parseErr := parseOrderedJSONObjectBytes(element)
		if parseErr != nil {
			return false, parseErr
		}
		hasScope, err := openUPMEntryHasPackageScope(entry)
		if err != nil {
			return false, err
		}
		if hasScope {
			return true, nil
		}
	}
	return false, nil
}

func openUPMEntryHasPackageScope(entry orderedJSONObject) (bool, error) {
	urlRaw, hasURL := entry.values["url"]
	if !hasURL {
		return false, nil
	}
	url := ""
	if unmarshalErr := json.Unmarshal(urlRaw, &url); unmarshalErr != nil {
		return false, unmarshalErr
	}
	if url != openUPMRegistryURL {
		return false, nil
	}
	rawScopes, hasScopes := entry.values["scopes"]
	if !hasScopes {
		return false, nil
	}
	scopes := []string{}
	if unmarshalErr := json.Unmarshal(rawScopes, &scopes); unmarshalErr != nil {
		return false, unmarshalErr
	}
	for _, scope := range scopes {
		if scope == dispatcherUnityPackageName {
			return true, nil
		}
	}
	return false, nil
}

func writePackageManifestAtomically(manifestPath string, content []byte) error {
	temporaryPath := manifestPath + ".tmp"
	if err := os.WriteFile(temporaryPath, content, 0o644); err != nil {
		return err
	}
	if err := os.Rename(temporaryPath, manifestPath); err != nil {
		_ = os.Remove(temporaryPath)
		return err
	}
	return nil
}

func writePackageInstallSuccess(stdout io.Writer, result packageManifestMergeResult, version string) {
	if result.RegistryAdded || result.ScopeAdded {
		clicore.WriteFormat(
			stdout,
			"Added scoped registry %s (scope %s)\n",
			openUPMRegistryURL,
			dispatcherUnityPackageName,
		)
	}
	if result.PreviousVersion != "" {
		clicore.WriteFormat(
			stdout,
			"Updated %s %s -> %s in %s\n",
			dispatcherUnityPackageName,
			result.PreviousVersion,
			version,
			dispatcherPackagesManifestRelativePath,
		)
	} else {
		clicore.WriteFormat(
			stdout,
			"Added %s %s to %s\n",
			dispatcherUnityPackageName,
			version,
			dispatcherPackagesManifestRelativePath,
		)
	}
	clicore.WriteLine(stdout, "If the Unity Editor is open, it applies the change when the window regains focus; otherwise the next launch applies it.")
}

func packageManifestInvalidError(projectRoot string, cause error) clierrors.CLIError {
	return clierrors.CLIError{
		ErrorCode:   clierrors.ErrorCodePackageManifestInvalid,
		Phase:       clierrors.ErrorPhaseExecution,
		Message:     "Packages/manifest.json is missing or invalid",
		Retryable:   false,
		SafeToRetry: false,
		Command:     clicore.PackageCommandName,
		NextActions: []string{
			"Confirm the path is a Unity project root that contains Packages/manifest.json.",
			"Repair Packages/manifest.json so it is valid JSON, then retry.",
		},
		Details: map[string]any{
			"ProjectRoot": projectRoot,
			"Cause":       cause.Error(),
		},
	}
}

func packageRegistryUnavailableError(projectRoot string, cause error) clierrors.CLIError {
	return clierrors.CLIError{
		ErrorCode:   clierrors.ErrorCodePackageRegistryUnavailable,
		Phase:       clierrors.ErrorPhaseExecution,
		Message:     "OpenUPM registry lookup failed",
		Retryable:   true,
		SafeToRetry: true,
		Command:     clicore.PackageCommandName,
		NextActions: []string{
			"Retry after the network is available.",
			"Pin a version with `uloop package install --version <x.y.z>` to skip the registry lookup.",
		},
		Details: map[string]any{
			"ProjectRoot": projectRoot,
			"Cause":       cause.Error(),
		},
	}
}

func printPackageHelp(stdout io.Writer) {
	clicore.WriteLine(stdout, "Usage:")
	clicore.WriteLine(stdout, "  uloop package install [--version <x.y.z>]")
	clicore.WriteLine(stdout, "  uloop package status")
	clicore.WriteLine(stdout, "")
	clicore.WriteLine(stdout, "Install or inspect the Unity CLI Loop package in a Unity project's Packages/manifest.json.")
	clicore.WriteLine(stdout, "")
	clicore.WriteLine(stdout, "Subcommands:")
	clicore.WriteLine(stdout, "  install   Add the OpenUPM scoped registry and package dependency")
	clicore.WriteLine(stdout, "  status    Report whether the registry and dependency are present")
	clicore.WriteLine(stdout, "")
	clicore.WriteLine(stdout, "Options:")
	clicore.WriteLine(stdout, "  --version <x.y.z>   Install a specific package version instead of dist-tags.latest")
	clicore.WriteLine(stdout, "")
	printGlobalOptionsHelp(stdout)
}

func bytesReplaceCRLF(content []byte) []byte {
	return bytes.ReplaceAll(content, []byte("\r\n"), []byte("\n"))
}
