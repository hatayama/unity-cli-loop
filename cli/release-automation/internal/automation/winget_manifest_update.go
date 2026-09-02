package automation

import (
	"context"
	"encoding/json"
	"flag"
	"fmt"
	"io"
	"os"
	"strings"
	"text/template"
)

const (
	wingetPkgsTokenEnvName          = "WINGET_PKGS_TOKEN"
	wingetPackageIdentifier         = "hatayama.uloop"
	wingetWindowsAmd64AssetName     = "uloop-dispatcher-windows-amd64.zip"
	wingetUpstreamRepo              = "microsoft/winget-pkgs"
	wingetUpstreamBranch            = "master"
	wingetManifestSchemaVersion     = "1.10.0"
	wingetVersionManifestFilename   = "hatayama.uloop.yaml"
	wingetInstallerManifestFilename = "hatayama.uloop.installer.yaml"
	wingetLocaleManifestFilename    = "hatayama.uloop.locale.en-US.yaml"
)

// winget-pkgs validation rejects manifests at ManifestVersion 1.7.0 or later
// with SchemaHeaderNotFound when the yaml-language-server schema header is
// missing, so every generated manifest must start with it.
const wingetVersionManifestTemplate = `# yaml-language-server: $schema=https://aka.ms/winget-manifest.version.` + wingetManifestSchemaVersion + `.schema.json
PackageIdentifier: hatayama.uloop
PackageVersion: {{.Version}}
DefaultLocale: en-US
ManifestType: version
ManifestVersion: ` + wingetManifestSchemaVersion + `
`

const wingetInstallerManifestTemplate = `# yaml-language-server: $schema=https://aka.ms/winget-manifest.installer.` + wingetManifestSchemaVersion + `.schema.json
PackageIdentifier: hatayama.uloop
PackageVersion: {{.Version}}
InstallerType: zip
NestedInstallerType: portable
NestedInstallerFiles:
  - RelativeFilePath: uloop.exe
    PortableCommandAlias: uloop
Commands:
  - uloop
ReleaseDate: {{.ReleaseDate}}
Installers:
  - Architecture: x64
    InstallerUrl: https://github.com/{{.Repository}}/releases/download/dispatcher-v{{.Version}}/uloop-dispatcher-windows-amd64.zip
    InstallerSha256: {{.SHA256Upper}}
ManifestType: installer
ManifestVersion: ` + wingetManifestSchemaVersion + `
`

const wingetLocaleManifestTemplate = `# yaml-language-server: $schema=https://aka.ms/winget-manifest.defaultLocale.` + wingetManifestSchemaVersion + `.schema.json
PackageIdentifier: hatayama.uloop
PackageVersion: {{.Version}}
PackageLocale: en-US
Publisher: hatayama
PublisherUrl: https://github.com/hatayama
PublisherSupportUrl: https://github.com/hatayama/unity-cli-loop/issues
PackageName: Unity CLI Loop
PackageUrl: https://github.com/hatayama/unity-cli-loop
License: MIT
LicenseUrl: https://github.com/hatayama/unity-cli-loop/blob/main/LICENSE
ShortDescription: Let AI drive Unity, from Editor to Play Mode
Moniker: uloop
Tags:
  - unity
  - cli
  - ai
ReleaseNotesUrl: https://github.com/{{.Repository}}/releases/tag/dispatcher-v{{.Version}}
ManifestType: defaultLocale
ManifestVersion: ` + wingetManifestSchemaVersion + `
`

type wingetManifestUpdateConfig struct {
	repository string
	tag        string
	forkRepo   string
}

type wingetManifestUpdateDeps struct {
	runOutput func(ctx context.Context, extraEnv []string, name string, args ...string) (string, error)
}

type wingetManifestData struct {
	Version     string
	Repository  string
	SHA256Upper string
	ReleaseDate string
}

type wingetReleaseMetadata struct {
	PublishedAt  string `json:"publishedAt"`
	IsPrerelease bool   `json:"isPrerelease"`
}

// RunUpdateWingetManifest opens a winget-pkgs pull request for a stable dispatcher release.
func RunUpdateWingetManifest(ctx context.Context, stdout io.Writer, stderr io.Writer, args []string) int {
	config, err := parseWingetManifestUpdateFlags(args)
	if err != nil {
		writeWingetManifestUpdateLine(stderr, "update-winget-manifest:", err)
		return 1
	}
	return runUpdateWingetManifestWithDeps(ctx, stdout, stderr, config, defaultWingetManifestUpdateDeps())
}

func defaultWingetManifestUpdateDeps() wingetManifestUpdateDeps {
	return wingetManifestUpdateDeps{runOutput: runHomebrewFormulaUpdateCommandOutput}
}

func parseWingetManifestUpdateFlags(args []string) (wingetManifestUpdateConfig, error) {
	flagSet := flag.NewFlagSet("update-winget-manifest", flag.ContinueOnError)
	repository := flagSet.String("repo", "", "GitHub repository that owns the dispatcher release")
	tag := flagSet.String("tag", "", "dispatcher release tag such as dispatcher-v3.1.0")
	forkRepo := flagSet.String("fork-repo", "", "winget-pkgs fork repository in owner/name form")
	err := flagSet.Parse(args)
	if err != nil {
		return wingetManifestUpdateConfig{}, err
	}
	if *repository == "" {
		return wingetManifestUpdateConfig{}, fmt.Errorf("--repo is required")
	}
	if *tag == "" {
		return wingetManifestUpdateConfig{}, fmt.Errorf("--tag is required")
	}
	if *forkRepo == "" {
		return wingetManifestUpdateConfig{}, fmt.Errorf("--fork-repo is required")
	}
	return wingetManifestUpdateConfig{repository: *repository, tag: *tag, forkRepo: *forkRepo}, nil
}

func runUpdateWingetManifestWithDeps(
	ctx context.Context,
	stdout io.Writer,
	stderr io.Writer,
	config wingetManifestUpdateConfig,
	deps wingetManifestUpdateDeps,
) int {
	// Releases must stay green until the winget-pkgs PAT is configured.
	token := strings.TrimSpace(os.Getenv(wingetPkgsTokenEnvName))
	if token == "" {
		writeWingetManifestUpdateLine(stdout, "WINGET_PKGS_TOKEN is not configured; skipping winget manifest update.")
		return 0
	}

	version, err := dispatcherVersionFromReleaseTag(config.tag)
	if err != nil {
		return writeWingetManifestUpdateError(stderr, err)
	}
	if strings.Contains(version, "-") {
		writeWingetManifestUpdateLine(stdout, fmt.Sprintf("dispatcher %s is a pre-release; winget receives stable releases only. Skipping.", version))
		return 0
	}

	versionPath := wingetPackageManifestPath() + "/" + version
	versionExists, err := wingetUpstreamPathExists(ctx, deps, token, versionPath)
	if err != nil {
		return writeWingetManifestUpdateError(stderr, err)
	}
	if versionExists {
		writeWingetManifestUpdateLine(stdout, fmt.Sprintf("winget manifest for %s already exists upstream; skipping.", version))
		return 0
	}
	metadata, err := downloadWingetReleaseMetadata(ctx, deps, config.repository, config.tag)
	if err != nil {
		return writeWingetManifestUpdateError(stderr, err)
	}
	if metadata.IsPrerelease {
		writeWingetManifestUpdateLine(stdout, fmt.Sprintf("GitHub release %s is marked as a pre-release; winget receives stable releases only. Skipping.", config.tag))
		return 0
	}
	data, err := loadWingetManifestData(ctx, deps, config, version, metadata.PublishedAt)
	if err != nil {
		return writeWingetManifestUpdateError(stderr, err)
	}

	packageExists, err := wingetUpstreamPathExists(ctx, deps, token, wingetPackageManifestPath())
	if err != nil {
		return writeWingetManifestUpdateError(stderr, err)
	}
	manifests, err := renderWingetManifests(data)
	if err != nil {
		return writeWingetManifestUpdateError(stderr, err)
	}
	branch := "hatayama-uloop-" + version
	if err = publishWingetManifestBranch(ctx, deps, token, config.forkRepo, branch, versionPath, version, manifests); err != nil {
		return writeWingetManifestUpdateError(stderr, err)
	}

	forkOwner := strings.SplitN(config.forkRepo, "/", 2)[0]
	pullRequestOpen, err := wingetPullRequestOpen(ctx, deps, token, forkOwner, branch)
	if err != nil {
		return writeWingetManifestUpdateError(stderr, err)
	}
	if pullRequestOpen {
		writeWingetManifestUpdateLine(stdout, "winget-pkgs pull request is already open; skipping.")
		return 0
	}

	title := wingetPullRequestTitle(packageExists, version)
	body := "Automated submission from https://github.com/" + config.repository + "/releases/tag/" + config.tag
	pullRequestURL, err := openWingetPullRequest(ctx, deps, token, forkOwner, branch, title, body)
	if err != nil {
		return writeWingetManifestUpdateError(stderr, err)
	}
	writeWingetManifestUpdateLine(stdout, pullRequestURL)
	return 0
}

func loadWingetManifestData(
	ctx context.Context,
	deps wingetManifestUpdateDeps,
	config wingetManifestUpdateConfig,
	version string,
	releaseDate string,
) (wingetManifestData, error) {
	sha256, err := downloadWingetAssetSHA256(ctx, deps, config.repository, config.tag)
	if err != nil {
		return wingetManifestData{}, err
	}
	return wingetManifestData{
		Version:     version,
		Repository:  config.repository,
		SHA256Upper: strings.ToUpper(sha256),
		ReleaseDate: releaseDate,
	}, nil
}

func downloadWingetAssetSHA256(
	ctx context.Context,
	deps wingetManifestUpdateDeps,
	repository string,
	tag string,
) (string, error) {
	output, err := deps.runOutput(
		ctx,
		nil,
		"gh",
		"release",
		"download",
		tag,
		"--repo",
		repository,
		"--pattern",
		wingetWindowsAmd64AssetName+".sha256",
		"--output",
		"-",
	)
	if err != nil {
		return "", err
	}
	return parseSha256Asset(output, wingetWindowsAmd64AssetName)
}

func downloadWingetReleaseMetadata(
	ctx context.Context,
	deps wingetManifestUpdateDeps,
	repository string,
	tag string,
) (wingetReleaseMetadata, error) {
	output, err := deps.runOutput(
		ctx,
		nil,
		"gh",
		"release",
		"view",
		tag,
		"--repo",
		repository,
		"--json",
		"publishedAt,isPrerelease",
	)
	if err != nil {
		return wingetReleaseMetadata{}, err
	}
	metadata := wingetReleaseMetadata{}
	if err = json.Unmarshal([]byte(output), &metadata); err != nil {
		return wingetReleaseMetadata{}, fmt.Errorf("failed to parse release metadata for %s: %w", tag, err)
	}
	if len(metadata.PublishedAt) < len("2006-01-02") {
		return wingetReleaseMetadata{}, fmt.Errorf("release %s has an invalid publishedAt value %q", tag, metadata.PublishedAt)
	}
	metadata.PublishedAt = metadata.PublishedAt[:len("2006-01-02")]
	return metadata, nil
}

func renderWingetManifests(data wingetManifestData) (map[string]string, error) {
	templates := map[string]string{
		wingetVersionManifestFilename:   wingetVersionManifestTemplate,
		wingetInstallerManifestFilename: wingetInstallerManifestTemplate,
		wingetLocaleManifestFilename:    wingetLocaleManifestTemplate,
	}
	manifests := make(map[string]string, len(templates))
	for filename, source := range templates {
		content, err := renderWingetManifest(filename, source, data)
		if err != nil {
			return nil, err
		}
		manifests[filename] = content
	}
	return manifests, nil
}

func renderWingetManifest(name string, source string, data wingetManifestData) (string, error) {
	parsed, err := template.New(name).Parse(source)
	if err != nil {
		return "", err
	}
	builder := strings.Builder{}
	if err = parsed.Execute(&builder, data); err != nil {
		return "", err
	}
	return builder.String(), nil
}

func publishWingetManifestBranch(
	ctx context.Context,
	deps wingetManifestUpdateDeps,
	token string,
	forkRepo string,
	branch string,
	versionPath string,
	version string,
	manifests map[string]string,
) error {
	if err := syncWingetFork(ctx, deps, token, forkRepo); err != nil {
		return err
	}
	masterSHA, err := wingetUpstreamMasterSHA(ctx, deps, token)
	if err != nil {
		return err
	}
	if err = ensureWingetBranch(ctx, deps, token, forkRepo, branch, masterSHA); err != nil {
		return err
	}
	filenames := []string{
		wingetVersionManifestFilename,
		wingetInstallerManifestFilename,
		wingetLocaleManifestFilename,
	}
	for _, filename := range filenames {
		path := versionPath + "/" + filename
		if err = putWingetManifestFile(ctx, deps, token, forkRepo, branch, path, version, manifests[filename]); err != nil {
			return err
		}
	}
	return nil
}

func wingetPullRequestTitle(packageExists bool, version string) string {
	prefix := "New package:"
	if packageExists {
		prefix = "New version:"
	}
	return fmt.Sprintf("%s %s version %s", prefix, wingetPackageIdentifier, version)
}

func wingetPackageManifestPath() string {
	return "manifests/h/hatayama/uloop"
}

func writeWingetManifestUpdateError(stderr io.Writer, err error) int {
	writeWingetManifestUpdateLine(stderr, "update-winget-manifest:", err)
	return 1
}

func writeWingetManifestUpdateLine(writer io.Writer, values ...any) {
	// CI status output failures cannot be recovered after the command outcome is known.
	_, _ = fmt.Fprintln(writer, values...)
}
