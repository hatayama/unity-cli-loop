package automation

import (
	"archive/tar"
	"bytes"
	"compress/gzip"
	"os"
	"path/filepath"
	"strings"
	"testing"
)

// validSignedUnityPackageEntries is the smallest tarball the staging step accepts: the manifest,
// a non-empty signature, a Unity .meta file, and a skill under the tilde-suffixed CLI-only folder.
func validSignedUnityPackageEntries() map[string]string {
	return map[string]string{
		"package/package.json":                               `{"name": "io.github.hatayama.uloopmcp", "version": "3.14.0"}`,
		"package/package.json.meta":                          "fileFormatVersion: 2",
		"package/.attestation.p7m":                           "signature",
		"package/Editor/CliOnlyTools~/Launch/Skill/SKILL.md": "# launch",
	}
}

func writeSignedUnityPackageArchive(t *testing.T, directory string, fileName string, entries map[string]string) {
	t.Helper()
	var buffer bytes.Buffer
	gzipWriter := gzip.NewWriter(&buffer)
	tarWriter := tar.NewWriter(gzipWriter)
	for name, content := range entries {
		header := &tar.Header{Name: name, Mode: 0o644, Size: int64(len(content)), Typeflag: tar.TypeReg}
		if err := tarWriter.WriteHeader(header); err != nil {
			t.Fatalf("write tar header: %v", err)
		}
		if _, err := tarWriter.Write([]byte(content)); err != nil {
			t.Fatalf("write tar entry: %v", err)
		}
	}
	if err := tarWriter.Close(); err != nil {
		t.Fatalf("close tar: %v", err)
	}
	if err := gzipWriter.Close(); err != nil {
		t.Fatalf("close gzip: %v", err)
	}
	if err := os.WriteFile(filepath.Join(directory, fileName), buffer.Bytes(), 0o600); err != nil {
		t.Fatalf("write archive: %v", err)
	}
}

func runStageSignedUnityPackageForTest(directory string, version string) (int, string, string) {
	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := RunStageSignedUnityPackage(&stdout, &stderr, []string{"--dir", directory, "--version", version})
	return code, stdout.String(), stderr.String()
}

// Verifies a valid signed tarball is renamed to the release asset name and its path is printed
// as a GITHUB_OUTPUT line.
func TestStageSignedUnityPackageRenamesValidTarball(t *testing.T) {
	directory := t.TempDir()
	writeSignedUnityPackageArchive(t, directory, "upm-output.tgz", validSignedUnityPackageEntries())

	code, stdout, stderr := runStageSignedUnityPackageForTest(directory, "3.14.0")
	if code != 0 {
		t.Fatalf("exit code = %d, stderr = %s", code, stderr)
	}
	wantPath := filepath.Join(directory, "io.github.hatayama.uloopmcp-3.14.0.tgz")
	if stdout != "archive="+wantPath+"\n" {
		t.Fatalf("stdout = %q, want archive=%s", stdout, wantPath)
	}
	if _, err := os.Stat(wantPath); err != nil {
		t.Fatalf("staged archive missing: %v", err)
	}
	if _, err := os.Stat(filepath.Join(directory, "upm-output.tgz")); !os.IsNotExist(err) {
		t.Fatalf("original archive still present: %v", err)
	}
}

// Verifies each tarball defect that would ship a broken or unsigned package to OpenUPM is
// rejected and leaves no staged release asset behind.
func TestStageSignedUnityPackageRejectsDefectiveTarballs(t *testing.T) {
	tests := []struct {
		name    string
		edit    func(entries map[string]string)
		version string
		wantErr string
	}{
		{
			name:    "missing signature",
			edit:    func(entries map[string]string) { delete(entries, "package/.attestation.p7m") },
			wantErr: ".attestation.p7m",
		},
		{
			name:    "empty signature",
			edit:    func(entries map[string]string) { entries["package/.attestation.p7m"] = "" },
			wantErr: ".attestation.p7m",
		},
		{
			name:    "missing manifest",
			edit:    func(entries map[string]string) { delete(entries, "package/package.json") },
			wantErr: "package/package.json",
		},
		{
			name: "different package name",
			edit: func(entries map[string]string) {
				entries["package/package.json"] = `{"name": "com.example.other", "version": "3.14.0"}`
			},
			wantErr: "com.example.other",
		},
		{
			name:    "manifest that is not JSON",
			edit:    func(entries map[string]string) { entries["package/package.json"] = "name: io.github.hatayama.uloopmcp" },
			wantErr: "parse package/package.json",
		},
		{
			name:    "version differs from the release tag",
			edit:    func(map[string]string) {},
			version: "3.15.0",
			wantErr: "3.14.0",
		},
		{
			name:    "no Unity meta files",
			edit:    func(entries map[string]string) { delete(entries, "package/package.json.meta") },
			wantErr: ".meta",
		},
		{
			name: "CLI-only skills folder dropped",
			edit: func(entries map[string]string) {
				delete(entries, "package/Editor/CliOnlyTools~/Launch/Skill/SKILL.md")
			},
			wantErr: "CliOnlyTools~",
		},
	}

	for _, test := range tests {
		t.Run(test.name, func(t *testing.T) {
			directory := t.TempDir()
			entries := validSignedUnityPackageEntries()
			test.edit(entries)
			writeSignedUnityPackageArchive(t, directory, "upm-output.tgz", entries)
			version := test.version
			if version == "" {
				version = "3.14.0"
			}

			code, stdout, stderr := runStageSignedUnityPackageForTest(directory, version)
			if code == 0 {
				t.Fatalf("exit code = 0, stdout = %q; want a failure", stdout)
			}
			if !strings.Contains(stderr, test.wantErr) {
				t.Fatalf("stderr = %q, want it to mention %q", stderr, test.wantErr)
			}
			if _, err := os.Stat(filepath.Join(directory, signedUnityPackageAssetName(version))); !os.IsNotExist(err) {
				t.Fatalf("defective archive was staged: %v", err)
			}
		})
	}
}

// Verifies the staging step refuses to guess when the pack output directory holds no tarball or
// more than one.
func TestStageSignedUnityPackageRequiresExactlyOneTarball(t *testing.T) {
	t.Run("none", func(t *testing.T) {
		code, _, stderr := runStageSignedUnityPackageForTest(t.TempDir(), "3.14.0")
		if code == 0 || !strings.Contains(stderr, "found 0") {
			t.Fatalf("exit code = %d, stderr = %q; want a failure reporting 0 tarballs", code, stderr)
		}
	})
	t.Run("two", func(t *testing.T) {
		directory := t.TempDir()
		writeSignedUnityPackageArchive(t, directory, "first.tgz", validSignedUnityPackageEntries())
		writeSignedUnityPackageArchive(t, directory, "second.tar.gz", validSignedUnityPackageEntries())
		code, _, stderr := runStageSignedUnityPackageForTest(directory, "3.14.0")
		if code == 0 || !strings.Contains(stderr, "found 2") {
			t.Fatalf("exit code = %d, stderr = %q; want a failure reporting 2 tarballs", code, stderr)
		}
	})
}

// Verifies pack output that is missing or not a gzip tar archive fails the staging step instead of
// being attached to the release.
func TestStageSignedUnityPackageRejectsUnreadablePackOutput(t *testing.T) {
	t.Run("missing directory", func(t *testing.T) {
		code, _, stderr := runStageSignedUnityPackageForTest(filepath.Join(t.TempDir(), "absent"), "3.14.0")
		if code == 0 || !strings.Contains(stderr, "read pack output directory") {
			t.Fatalf("exit code = %d, stderr = %q; want a failure reading the directory", code, stderr)
		}
	})
	t.Run("not gzip", func(t *testing.T) {
		directory := t.TempDir()
		if err := os.WriteFile(filepath.Join(directory, "upm-output.tgz"), []byte("not an archive"), 0o600); err != nil {
			t.Fatalf("write archive: %v", err)
		}
		code, _, stderr := runStageSignedUnityPackageForTest(directory, "3.14.0")
		if code == 0 || !strings.Contains(stderr, "read signed tarball") {
			t.Fatalf("exit code = %d, stderr = %q; want a failure reading the tarball", code, stderr)
		}
	})
	t.Run("gzip without a tar inside", func(t *testing.T) {
		directory := t.TempDir()
		var buffer bytes.Buffer
		gzipWriter := gzip.NewWriter(&buffer)
		if _, err := gzipWriter.Write(bytes.Repeat([]byte("x"), 1024)); err != nil {
			t.Fatalf("write gzip: %v", err)
		}
		if err := gzipWriter.Close(); err != nil {
			t.Fatalf("close gzip: %v", err)
		}
		if err := os.WriteFile(filepath.Join(directory, "upm-output.tgz"), buffer.Bytes(), 0o600); err != nil {
			t.Fatalf("write archive: %v", err)
		}
		code, _, stderr := runStageSignedUnityPackageForTest(directory, "3.14.0")
		if code == 0 || !strings.Contains(stderr, "read signed tarball") {
			t.Fatalf("exit code = %d, stderr = %q; want a failure reading the tarball", code, stderr)
		}
	})
}

// Verifies missing or unknown arguments exit with the usage status.
func TestRunStageSignedUnityPackageRejectsBadArguments(t *testing.T) {
	for _, args := range [][]string{
		{"--version", "3.14.0"},
		{"--dir", "signed"},
		{"--dir", "signed", "--version", "3.14.0", "--unknown"},
	} {
		var stdout bytes.Buffer
		var stderr bytes.Buffer
		code := RunStageSignedUnityPackage(&stdout, &stderr, args)
		if code != 2 || stdout.Len() != 0 {
			t.Fatalf("args %v: exit code = %d, stdout = %q; want 2 without output", args, code, stdout.String())
		}
	}
}
