package automation

import (
	"context"
	"crypto/sha256"
	"errors"
	"fmt"
	"strings"
	"testing"

	"github.com/hatayama/unity-cli-loop/dispatcher/attestation"
)

func TestValidateDispatcherPinOfflineRejectsManifestWithoutRequiredArchive(t *testing.T) {
	// Verifies a pin cannot authorize bootstrap when one supported platform archive is absent.
	pin := []byte(`{"projectRunnerVersion":"3.0.0-beta.47","minimumDispatcherVersion":"3.0.1-beta.6","dispatcherReleaseTag":"dispatcher-v3.0.1-beta.6","dispatcherArchiveManifest":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa  install.sh\nbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb  install.ps1\ncccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc  uloop-dispatcher-darwin-arm64.tar.gz\ndddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd  uloop-dispatcher-darwin-amd64.tar.gz"}`)

	err := ValidateDispatcherPinOffline(pin, pin)

	if err == nil {
		t.Fatal("expected required Windows archive failure")
	}
}

func TestValidateDispatcherPinOfflineRejectsManifestWithoutLinuxArchive(t *testing.T) {
	// Verifies a pin that omits the published Linux archive cannot authorize bootstrap.
	pin := []byte(`{"projectRunnerVersion":"3.0.0-beta.47","minimumDispatcherVersion":"3.0.1-beta.6","dispatcherReleaseTag":"dispatcher-v3.0.1-beta.6","dispatcherArchiveManifest":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa  install.sh\nbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb  install.ps1\ncccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc  uloop-dispatcher-darwin-amd64.tar.gz\ndddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd  uloop-dispatcher-darwin-arm64.tar.gz\neeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee  uloop-dispatcher-windows-amd64.zip"}`)

	err := ValidateDispatcherPinOffline(pin, pin)

	if err == nil {
		t.Fatal("expected required Linux archive failure")
	}
	if !strings.Contains(err.Error(), "uloop-dispatcher-linux-amd64.tar.gz") {
		t.Fatalf("error = %q, want it to name the missing Linux archive", err)
	}
}

func TestValidateDispatcherPinOfflineRejectsUnsortedManifest(t *testing.T) {
	// Verifies the guard requires canonical manifest order so a re-stamp is byte reproducible.
	pin := []byte(`{"projectRunnerVersion":"3.0.0-beta.47","minimumDispatcherVersion":"3.0.1-beta.6","dispatcherReleaseTag":"dispatcher-v3.0.1-beta.6","dispatcherArchiveManifest":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb  install.ps1\naaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa  install.sh\ncccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc  uloop-dispatcher-darwin-amd64.tar.gz\ndddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd  uloop-dispatcher-darwin-arm64.tar.gz\neeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee  uloop-dispatcher-windows-amd64.zip\nffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff  uloop-dispatcher-linux-amd64.tar.gz"}`)

	err := ValidateDispatcherPinOffline(pin, pin)

	if err == nil {
		t.Fatal("expected unsorted manifest failure")
	}
}

func TestValidateDispatcherPinOfflineRejectsMismatchedProjectCopy(t *testing.T) {
	// Verifies the project mirror cannot silently diverge from the package trust root.
	packagePin := validDispatcherPinGuardFixture()
	projectPin := []byte(`{"projectRunnerVersion":"3.0.0-beta.47"}`)

	err := ValidateDispatcherPinOffline(packagePin, projectPin)

	if err == nil {
		t.Fatal("expected mismatched project pin failure")
	}
}

func TestValidateDispatcherPinOfflineRejectsMalformedManifest(t *testing.T) {
	// Verifies malformed digest entries cannot become package bootstrap trust inputs.
	pin := []byte(`{"projectRunnerVersion":"3.0.0-beta.47","minimumDispatcherVersion":"3.0.1-beta.6","dispatcherReleaseTag":"dispatcher-v3.0.1-beta.6","dispatcherArchiveManifest":"not-a-digest  install.sh"}`)

	err := ValidateDispatcherPinOffline(pin, pin)

	if err == nil {
		t.Fatal("expected malformed manifest failure")
	}
}

func TestValidateDispatcherPinOfflineRejectsPinnedVersionBelowMinimum(t *testing.T) {
	// Verifies bootstrap never stamps a dispatcher release that the package immediately rejects.
	pin := []byte(`{"projectRunnerVersion":"3.0.0-beta.47","minimumDispatcherVersion":"3.0.2","dispatcherReleaseTag":"dispatcher-v3.0.1-beta.6","dispatcherArchiveManifest":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa  install.sh\nbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb  install.ps1\ncccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc  uloop-dispatcher-darwin-amd64.tar.gz\ndddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd  uloop-dispatcher-darwin-arm64.tar.gz\neeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee  uloop-dispatcher-windows-amd64.zip\nffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff  uloop-dispatcher-linux-amd64.tar.gz"}`)

	err := ValidateDispatcherPinOffline(pin, pin)

	if err == nil {
		t.Fatal("expected pinned version below minimum failure")
	}
}

func TestVerifyDispatcherPinSubjectsRejectsManifestThatDoesNotExactlyMatchSubjects(t *testing.T) {
	// Verifies the network guard rejects both omitted and unverified release subjects.
	pin := validDispatcherPinGuardFixture()
	deps := validDispatcherPinGuardDeps()
	deps.verifySubjects = func([]byte, string) (map[string]string, error) {
		return map[string]string{
			"install.sh":                           "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
			"install.ps1":                          "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
			"uloop-dispatcher-darwin-amd64.tar.gz": "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc",
			"uloop-dispatcher-darwin-arm64.tar.gz": "dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd",
			"uloop-dispatcher-windows-amd64.zip":   "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee",
			"uloop-dispatcher-linux-amd64.tar.gz":  "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff",
			"uloop-dispatcher-unexpected.tar.gz":   "9999999999999999999999999999999999999999999999999999999999999999",
		}, nil
	}

	err := verifyDispatcherPinSubjects(context.Background(), pin, deps)

	if err == nil {
		t.Fatal("expected exact subject-set mismatch failure")
	}
}

func TestVerifyDispatcherPinSubjectsFailsClosedWhenReleaseLookupFails(t *testing.T) {
	// Verifies GitHub API failures cannot skip published subject verification.
	pin := validDispatcherPinGuardFixture()
	deps := validDispatcherPinGuardDeps()
	deps.fetchReleaseAssets = func(context.Context, string) ([]dispatcherReleaseAsset, error) {
		return nil, errors.New("GitHub unavailable")
	}

	err := verifyDispatcherPinSubjects(context.Background(), pin, deps)

	if err == nil {
		t.Fatal("expected GitHub API failure")
	}
}

func TestDispatcherPinScriptDriftWarningsReportsButDoesNotFailForChangedSourceScript(t *testing.T) {
	// Verifies installer source drift is review information until a subsequent release can carry the new digest.
	warnings, err := DispatcherPinScriptDriftWarnings(validDispatcherPinGuardFixture(), map[string][]byte{
		"install.sh":  []byte("changed"),
		"install.ps1": []byte("changed"),
	})
	if err != nil {
		t.Fatalf("DispatcherPinScriptDriftWarnings failed: %v", err)
	}
	if len(warnings) != 2 {
		t.Fatalf("warning count = %d, want 2", len(warnings))
	}
}

func validDispatcherPinGuardDeps() dispatcherPinStampDeps {
	return dispatcherPinStampDeps{
		fetchReleaseAssets: func(context.Context, string) ([]dispatcherReleaseAsset, error) {
			return []dispatcherReleaseAsset{
				{Name: "install.sh", URL: "https://example.invalid/install.sh"},
				{Name: "install.ps1", URL: "https://example.invalid/install.ps1"},
				{Name: "uloop-dispatcher-darwin-amd64.tar.gz", URL: "https://example.invalid/darwin-amd64.tar.gz"},
				{Name: "uloop-dispatcher-darwin-arm64.tar.gz", URL: "https://example.invalid/darwin-arm64.tar.gz"},
				{Name: "uloop-dispatcher-windows-amd64.zip", URL: "https://example.invalid/windows-amd64.zip"},
				{Name: "uloop-dispatcher-linux-amd64.tar.gz", URL: "https://example.invalid/linux-amd64.tar.gz"},
			}, nil
		},
		fetchBundle:       func(context.Context, string) ([]byte, error) { return []byte("bundle"), nil },
		fetchTagCommitSHA: func(context.Context, string, string) (string, error) { return "commit", nil },
		verifySubjects: func([]byte, string) (map[string]string, error) {
			return map[string]string{
				"install.sh":                           "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
				"install.ps1":                          "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
				"uloop-dispatcher-darwin-amd64.tar.gz": "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc",
				"uloop-dispatcher-darwin-arm64.tar.gz": "dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd",
				"uloop-dispatcher-windows-amd64.zip":   "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee",
				"uloop-dispatcher-linux-amd64.tar.gz":  "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff",
			}, nil
		},
	}
}

func validDispatcherPinGuardFixture() []byte {
	return []byte(`{"projectRunnerVersion":"3.0.0-beta.47","minimumDispatcherVersion":"3.0.1-beta.6","dispatcherReleaseTag":"dispatcher-v3.0.1-beta.6","dispatcherArchiveManifest":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa  install.sh\nbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb  install.ps1\ncccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc  uloop-dispatcher-darwin-amd64.tar.gz\ndddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd  uloop-dispatcher-darwin-arm64.tar.gz\neeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee  uloop-dispatcher-windows-amd64.zip\nffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff  uloop-dispatcher-linux-amd64.tar.gz"}`)
}

func TestValidateDispatcherPinOfflineRejectsMalformedPinFields(t *testing.T) {
	// Verifies invalid JSON, a foreign tag prefix, non-semver versions, non-hex digests, and repeated assets are each rejected.
	digestA := strings.Repeat("a", 64)
	digestB := strings.Repeat("b", 64)
	cases := []struct {
		name    string
		pin     string
		wantErr string
	}{
		{"invalid JSON", "{", "is invalid JSON"},
		{"foreign tag prefix", `{"minimumDispatcherVersion":"3.0.0","dispatcherReleaseTag":"v3.0.0"}`, `dispatcherReleaseTag must start with "dispatcher-v"`},
		{"non-semver tag", `{"minimumDispatcherVersion":"3.0.0","dispatcherReleaseTag":"dispatcher-vlatest"}`, "dispatcher versions must be semver"},
		{"non-semver minimum", `{"minimumDispatcherVersion":"next","dispatcherReleaseTag":"dispatcher-v3.0.0"}`, "dispatcher versions must be semver"},
		{"non-hex digest", `{"minimumDispatcherVersion":"3.0.0","dispatcherReleaseTag":"dispatcher-v3.0.0","dispatcherArchiveManifest":"` + strings.Repeat("z", 64) + `  install.sh"}`, "invalid dispatcherArchiveManifest entry"},
		{"repeated asset", `{"minimumDispatcherVersion":"3.0.0","dispatcherReleaseTag":"dispatcher-v3.0.0","dispatcherArchiveManifest":"` + digestA + `  install.sh\n` + digestB + `  install.sh"}`, `repeats asset "install.sh"`},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			pin := []byte(testCase.pin)

			err := ValidateDispatcherPinOffline(pin, pin)

			if err == nil || !strings.Contains(err.Error(), testCase.wantErr) {
				t.Fatalf("expected error containing %q, got %v", testCase.wantErr, err)
			}
		})
	}
}

func TestValidateDispatcherPinOfflineAcceptsCompleteSortedManifest(t *testing.T) {
	// Verifies a byte-identical pin with a sorted manifest covering every required asset passes.
	pin := validDispatcherPinGuardFixture()

	if err := ValidateDispatcherPinOffline(pin, pin); err != nil {
		t.Fatalf("expected the valid fixture to pass, got %v", err)
	}
}

func TestVerifyDispatcherPinSubjectsRejectsInvalidPinBeforeFetching(t *testing.T) {
	// Verifies the exported verifier fails on an unparsable pin before contacting GitHub.
	err := VerifyDispatcherPinSubjects(context.Background(), []byte("{"))

	if err == nil || !strings.Contains(err.Error(), "is invalid JSON") {
		t.Fatalf("expected an invalid JSON error, got %v", err)
	}
}

func TestVerifyDispatcherPinSubjectsReportsEachVerificationStepFailure(t *testing.T) {
	// Verifies a missing installer, bundle, tag, and attestation failure each fail closed with a step-specific error.
	cases := []struct {
		name    string
		mutate  func(*dispatcherPinStampDeps)
		wantErr string
	}{
		{"missing installer", func(deps *dispatcherPinStampDeps) {
			deps.fetchReleaseAssets = func(context.Context, string) ([]dispatcherReleaseAsset, error) {
				return []dispatcherReleaseAsset{{Name: "install.ps1", URL: "https://example.invalid/install.ps1"}}, nil
			}
		}, `missing required asset "install.sh"`},
		{"bundle", func(deps *dispatcherPinStampDeps) {
			deps.fetchBundle = func(context.Context, string) ([]byte, error) { return nil, errors.New("no bundle") }
		}, "fetch dispatcher installer attestation bundle: no bundle"},
		{"tag commit", func(deps *dispatcherPinStampDeps) {
			deps.fetchTagCommitSHA = func(context.Context, string, string) (string, error) { return "", errors.New("no tag") }
		}, "resolve dispatcher release tag commit: no tag"},
		{"attestation", func(deps *dispatcherPinStampDeps) {
			deps.verifySubjects = func([]byte, string) (map[string]string, error) { return nil, errors.New("bad signature") }
		}, "verify dispatcher release attestation: bad signature"},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			deps := validDispatcherPinGuardDeps()
			testCase.mutate(&deps)

			err := verifyDispatcherPinSubjects(context.Background(), validDispatcherPinGuardFixture(), deps)

			if err == nil || !strings.Contains(err.Error(), testCase.wantErr) {
				t.Fatalf("expected error containing %q, got %v", testCase.wantErr, err)
			}
		})
	}
}

func TestVerifyDispatcherPinSubjectsComparesManifestWithVerifiedSubjects(t *testing.T) {
	// Verifies a pin matching the verified subjects passes and a pin with a different digest is rejected.
	deps := validDispatcherPinGuardDeps()
	if err := verifyDispatcherPinSubjects(context.Background(), validDispatcherPinGuardFixture(), deps); err != nil {
		t.Fatalf("expected the matching pin to pass, got %v", err)
	}

	alteredPin := []byte(strings.Replace(string(validDispatcherPinGuardFixture()), strings.Repeat("a", 64), strings.Repeat("0", 64), 1))
	err := verifyDispatcherPinSubjects(context.Background(), alteredPin, deps)

	if err == nil || !strings.Contains(err.Error(), "does not exactly match verified release subjects") {
		t.Fatalf("expected a manifest mismatch error, got %v", err)
	}
}

func TestDispatcherPinScriptDriftWarningsRejectsUnusableInputs(t *testing.T) {
	// Verifies an unparsable pin, a malformed manifest entry, and a missing source installer are errors rather than warnings.
	installers := map[string][]byte{"install.sh": []byte("a"), "install.ps1": []byte("b")}
	cases := []struct {
		name    string
		pin     []byte
		scripts map[string][]byte
		wantErr string
	}{
		{"invalid JSON", []byte("{"), installers, "is invalid JSON"},
		{"malformed entry", []byte(`{"dispatcherArchiveManifest":"no-separator"}`), installers, "invalid dispatcherArchiveManifest entry"},
		{"missing installer", validDispatcherPinGuardFixture(), map[string][]byte{"install.sh": []byte("a")}, `source installer "install.ps1" is unavailable`},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			_, err := DispatcherPinScriptDriftWarnings(testCase.pin, testCase.scripts)

			if err == nil || !strings.Contains(err.Error(), testCase.wantErr) {
				t.Fatalf("expected error containing %q, got %v", testCase.wantErr, err)
			}
		})
	}
}

func TestDispatcherPinScriptDriftWarningsIsSilentWhenScriptsMatchThePin(t *testing.T) {
	// Verifies installers whose digests match the pinned manifest, in any hex case, produce no warnings.
	installSh := []byte("echo install")
	installPs1 := []byte("Write-Host install")
	pin := fmt.Sprintf(`{"dispatcherReleaseTag":"dispatcher-v3.0.0","dispatcherArchiveManifest":"%X  install.sh\n%x  install.ps1"}`,
		sha256.Sum256(installSh), sha256.Sum256(installPs1))

	warnings, err := DispatcherPinScriptDriftWarnings([]byte(pin), map[string][]byte{"install.sh": installSh, "install.ps1": installPs1})
	if err != nil {
		t.Fatalf("DispatcherPinScriptDriftWarnings failed: %v", err)
	}
	if len(warnings) != 0 {
		t.Fatalf("expected no warnings, got %v", warnings)
	}
}

func TestVerifyDispatcherPinSubjectsVerifiesTheReleaseThePinNames(t *testing.T) {
	// Verifies the release assets, installer bundle, tag commit, and attestation are all looked up for the tag the pin records.
	deps := validDispatcherPinGuardDeps()
	baseAssets := deps.fetchReleaseAssets
	baseVerify := deps.verifySubjects
	var assetTag, bundleURL, commitRepository, commitTag, verifiedCommit string
	deps.fetchReleaseAssets = func(ctx context.Context, tag string) ([]dispatcherReleaseAsset, error) {
		assetTag = tag
		return baseAssets(ctx, tag)
	}
	deps.fetchBundle = func(_ context.Context, url string) ([]byte, error) {
		bundleURL = url
		return []byte("bundle"), nil
	}
	deps.fetchTagCommitSHA = func(_ context.Context, repository string, tag string) (string, error) {
		commitRepository = repository
		commitTag = tag
		return "pinned-commit", nil
	}
	deps.verifySubjects = func(bundle []byte, commit string) (map[string]string, error) {
		verifiedCommit = commit
		return baseVerify(bundle, commit)
	}

	if err := verifyDispatcherPinSubjects(context.Background(), validDispatcherPinGuardFixture(), deps); err != nil {
		t.Fatalf("verifyDispatcherPinSubjects failed: %v", err)
	}

	if assetTag != "dispatcher-v3.0.1-beta.6" || commitTag != "dispatcher-v3.0.1-beta.6" {
		t.Fatalf("asset tag = %q, commit tag = %q", assetTag, commitTag)
	}
	if bundleURL != "https://example.invalid/install.sh.sigstore.json" {
		t.Fatalf("bundle URL = %q", bundleURL)
	}
	if commitRepository != attestation.ReleaseRepository || verifiedCommit != "pinned-commit" {
		t.Fatalf("commit repository = %q, verified commit = %q", commitRepository, verifiedCommit)
	}
}
