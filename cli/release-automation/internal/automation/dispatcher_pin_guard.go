package automation

import (
	"bytes"
	"context"
	"crypto/sha256"
	"encoding/json"
	"fmt"
	"strings"

	sharedversion "github.com/hatayama/unity-cli-loop/common/version"
	"github.com/hatayama/unity-cli-loop/dispatcher/attestation"
)

const (
	dispatcherPinTagPrefix = "dispatcher-v"
	sha256HexLength        = 64
)

var requiredDispatcherPinAssets = []string{
	"install.sh",
	"install.ps1",
	"uloop-dispatcher-darwin-amd64.tar.gz",
	"uloop-dispatcher-darwin-arm64.tar.gz",
	"uloop-dispatcher-windows-amd64.zip",
}

type dispatcherPinGuardValues struct {
	MinimumDispatcherVersion  string `json:"minimumDispatcherVersion"`
	DispatcherReleaseTag      string `json:"dispatcherReleaseTag"`
	DispatcherArchiveManifest string `json:"dispatcherArchiveManifest"`
}

// ValidateDispatcherPinOffline validates the package pin without contacting GitHub.
func ValidateDispatcherPinOffline(packagePin []byte, projectPin []byte) error {
	if !bytes.Equal(packagePin, projectPin) {
		return fmt.Errorf("%s must match %s byte-for-byte", unityProjectCliPinFile, unityPackageCliPinFile)
	}
	values := dispatcherPinGuardValues{}
	if err := json.Unmarshal(packagePin, &values); err != nil {
		return fmt.Errorf("%s is invalid JSON: %w", unityPackageCliPinFile, err)
	}
	if !strings.HasPrefix(values.DispatcherReleaseTag, dispatcherPinTagPrefix) {
		return fmt.Errorf("%s dispatcherReleaseTag must start with %q", unityPackageCliPinFile, dispatcherPinTagPrefix)
	}
	pinnedVersion := strings.TrimPrefix(values.DispatcherReleaseTag, dispatcherPinTagPrefix)
	if !sharedversion.IsValid(pinnedVersion) || !sharedversion.IsValid(values.MinimumDispatcherVersion) {
		return fmt.Errorf("%s dispatcher versions must be semver", unityPackageCliPinFile)
	}
	if sharedversion.IsLessThan(pinnedVersion, values.MinimumDispatcherVersion) {
		return fmt.Errorf("%s dispatcherReleaseTag is older than minimumDispatcherVersion", unityPackageCliPinFile)
	}
	return validateDispatcherArchiveManifest(values.DispatcherArchiveManifest)
}

// VerifyDispatcherPinSubjects confirms the pinned manifest exactly matches the published verified subjects.
func VerifyDispatcherPinSubjects(ctx context.Context, packagePin []byte) error {
	return verifyDispatcherPinSubjects(ctx, packagePin, defaultDispatcherPinStampDeps())
}

// DispatcherPinScriptDriftWarnings reports source scripts that await a later dispatcher release and pin stamp.
func DispatcherPinScriptDriftWarnings(packagePin []byte, scripts map[string][]byte) ([]string, error) {
	values := dispatcherPinGuardValues{}
	if err := json.Unmarshal(packagePin, &values); err != nil {
		return nil, fmt.Errorf("%s is invalid JSON: %w", unityPackageCliPinFile, err)
	}
	manifestDigests := make(map[string]string)
	for _, entry := range strings.Split(values.DispatcherArchiveManifest, "\n") {
		digest, name, ok := strings.Cut(entry, "  ")
		if !ok {
			return nil, fmt.Errorf("%s has an invalid dispatcherArchiveManifest entry", unityPackageCliPinFile)
		}
		manifestDigests[name] = strings.ToLower(digest)
	}
	warnings := make([]string, 0)
	for _, scriptName := range []string{dispatcherPinStampInstallerScript, dispatcherPinStampPowerShellScript} {
		scriptData, exists := scripts[scriptName]
		if !exists {
			return nil, fmt.Errorf("source installer %q is unavailable", scriptName)
		}
		actualDigest := fmt.Sprintf("%x", sha256.Sum256(scriptData))
		if manifestDigests[scriptName] != actualDigest {
			warnings = append(warnings, fmt.Sprintf("pin release %s has a different %s; Unity first install uses the pinned script until the next dispatcher release and pin stamp", values.DispatcherReleaseTag, scriptName))
		}
	}
	return warnings, nil
}

func verifyDispatcherPinSubjects(
	ctx context.Context,
	packagePin []byte,
	deps dispatcherPinStampDeps,
) error {
	values := dispatcherPinGuardValues{}
	if err := json.Unmarshal(packagePin, &values); err != nil {
		return fmt.Errorf("%s is invalid JSON: %w", unityPackageCliPinFile, err)
	}
	assets, err := deps.fetchReleaseAssets(ctx, values.DispatcherReleaseTag)
	if err != nil {
		return fmt.Errorf("fetch dispatcher release assets: %w", err)
	}
	installerURL, err := findDispatcherReleaseAssetURL(assets, dispatcherPinStampInstallerScript)
	if err != nil {
		return err
	}
	bundleData, err := deps.fetchBundle(ctx, installerURL+".sigstore.json")
	if err != nil {
		return fmt.Errorf("fetch dispatcher installer attestation bundle: %w", err)
	}
	commitSHA, err := deps.fetchTagCommitSHA(ctx, attestation.ReleaseRepository, values.DispatcherReleaseTag)
	if err != nil {
		return fmt.Errorf("resolve dispatcher release tag commit: %w", err)
	}
	subjects, err := deps.verifySubjects(bundleData, commitSHA)
	if err != nil {
		return fmt.Errorf("verify dispatcher release attestation: %w", err)
	}
	manifest, err := buildDispatcherArchiveManifest(assets, subjects)
	if err != nil {
		return err
	}
	if values.DispatcherArchiveManifest != manifest {
		return fmt.Errorf("%s dispatcherArchiveManifest does not exactly match verified release subjects", unityPackageCliPinFile)
	}
	return nil
}

func validateDispatcherArchiveManifest(manifest string) error {
	if manifest == "" {
		return fmt.Errorf("%s dispatcherArchiveManifest is empty", unityPackageCliPinFile)
	}
	entries := strings.Split(manifest, "\n")
	assetNames := make(map[string]struct{}, len(entries))
	previous := ""
	for _, entry := range entries {
		digest, name, ok := strings.Cut(entry, "  ")
		if !ok || !isDispatcherPinSHA256(digest) || name == "" || strings.ContainsAny(name, "\r\n \t") {
			return fmt.Errorf("%s has an invalid dispatcherArchiveManifest entry", unityPackageCliPinFile)
		}
		if previous != "" && previous >= entry {
			return fmt.Errorf("%s dispatcherArchiveManifest is not canonically sorted", unityPackageCliPinFile)
		}
		previous = entry
		if _, exists := assetNames[name]; exists {
			return fmt.Errorf("%s dispatcherArchiveManifest repeats asset %q", unityPackageCliPinFile, name)
		}
		assetNames[name] = struct{}{}
	}
	for _, requiredAsset := range requiredDispatcherPinAssets {
		if _, exists := assetNames[requiredAsset]; !exists {
			return fmt.Errorf("%s dispatcherArchiveManifest is missing required asset %q", unityPackageCliPinFile, requiredAsset)
		}
	}
	return nil
}

func isDispatcherPinSHA256(value string) bool {
	if len(value) != sha256HexLength {
		return false
	}
	for _, character := range value {
		if (character < '0' || character > '9') && (character < 'a' || character > 'f') && (character < 'A' || character > 'F') {
			return false
		}
	}
	return true
}
