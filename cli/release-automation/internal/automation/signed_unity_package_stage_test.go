package automation

import (
	"archive/tar"
	"bytes"
	"compress/gzip"
	"fmt"
	"os"
	"path/filepath"
	"strings"
	"testing"
)

// signedUnityPackageSourceFiles is a package source tree with an asset and its .meta file beside
// package.json.meta, and a skill under the tilde-suffixed CLI-only folder.
var signedUnityPackageSourceFiles = map[string]string{
	"package.json":        `{"name": "io.github.hatayama.uloopmcp", "version": "3.14.0"}`,
	"package.json.meta":   "fileFormatVersion: 2",
	"Editor.meta":         "fileFormatVersion: 2",
	"Editor/Tool.cs":      "class Tool {}",
	"Editor/Tool.cs.meta": "fileFormatVersion: 2",
	"Editor/CliOnlyTools~/Launch/Skill/SKILL.md": "# launch",
}

func writeSignedUnityPackageSource(t *testing.T) string {
	t.Helper()
	sourceDirectory := t.TempDir()
	for relativePath, content := range signedUnityPackageSourceFiles {
		path := filepath.Join(sourceDirectory, filepath.FromSlash(relativePath))
		if err := os.MkdirAll(filepath.Dir(path), 0o755); err != nil {
			t.Fatalf("create source directory: %v", err)
		}
		if err := os.WriteFile(path, []byte(content), 0o600); err != nil {
			t.Fatalf("write source file: %v", err)
		}
	}
	return sourceDirectory
}

// validSignedUnityPackageEntries is what upm pack produces for signedUnityPackageSourceFiles: every
// source file under package/ plus a non-empty signature.
func validSignedUnityPackageEntries() map[string]string {
	entries := map[string]string{"package/.attestation.p7m": "signature"}
	for relativePath, content := range signedUnityPackageSourceFiles {
		entries["package/"+relativePath] = content
	}
	return entries
}

// writeSignedUnityPackageArchive writes entries as a gzip tar archive; a name ending in "/" becomes a
// directory entry.
func writeSignedUnityPackageArchive(t *testing.T, directory string, fileName string, entries map[string]string) {
	t.Helper()
	var buffer bytes.Buffer
	gzipWriter := gzip.NewWriter(&buffer)
	tarWriter := tar.NewWriter(gzipWriter)
	for name, content := range entries {
		header := &tar.Header{Name: name, Mode: 0o644, Size: int64(len(content)), Typeflag: tar.TypeReg}
		if strings.HasSuffix(name, "/") {
			header = &tar.Header{Name: name, Mode: 0o755, Typeflag: tar.TypeDir}
		}
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

func runStageSignedUnityPackageForTest(t *testing.T, directory string, version string) (int, string, string) {
	t.Helper()
	var stdout bytes.Buffer
	var stderr bytes.Buffer
	sourceDirectory := writeSignedUnityPackageSource(t)
	code := RunStageSignedUnityPackage(&stdout, &stderr, []string{"--dir", directory, "--version", version, "--source", sourceDirectory})
	return code, stdout.String(), stderr.String()
}

// Verifies a valid signed tarball is renamed to the release asset name and its path is printed
// as a GITHUB_OUTPUT line.
func TestStageSignedUnityPackageRenamesValidTarball(t *testing.T) {
	directory := t.TempDir()
	writeSignedUnityPackageArchive(t, directory, "upm-output.tgz", validSignedUnityPackageEntries())

	code, stdout, stderr := runStageSignedUnityPackageForTest(t, directory, "3.14.0")
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
			name:    "asset meta file dropped while package.json.meta is kept",
			edit:    func(entries map[string]string) { delete(entries, "package/Editor/Tool.cs.meta") },
			wantErr: "Editor/Tool.cs.meta",
		},
		{
			name: "CLI-only skill replaced by its bare directory",
			edit: func(entries map[string]string) {
				delete(entries, "package/Editor/CliOnlyTools~/Launch/Skill/SKILL.md")
				entries["package/Editor/CliOnlyTools~/"] = ""
			},
			wantErr: "Editor/CliOnlyTools~/Launch/Skill/SKILL.md",
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

			code, stdout, stderr := runStageSignedUnityPackageForTest(t, directory, version)
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
		code, _, stderr := runStageSignedUnityPackageForTest(t, t.TempDir(), "3.14.0")
		if code == 0 || !strings.Contains(stderr, "found 0") {
			t.Fatalf("exit code = %d, stderr = %q; want a failure reporting 0 tarballs", code, stderr)
		}
	})
	t.Run("two", func(t *testing.T) {
		directory := t.TempDir()
		writeSignedUnityPackageArchive(t, directory, "first.tgz", validSignedUnityPackageEntries())
		writeSignedUnityPackageArchive(t, directory, "second.tar.gz", validSignedUnityPackageEntries())
		code, _, stderr := runStageSignedUnityPackageForTest(t, directory, "3.14.0")
		if code == 0 || !strings.Contains(stderr, "found 2") {
			t.Fatalf("exit code = %d, stderr = %q; want a failure reporting 2 tarballs", code, stderr)
		}
	})
}

// Verifies pack output that is missing or not a gzip tar archive fails the staging step instead of
// being attached to the release.
func TestStageSignedUnityPackageRejectsUnreadablePackOutput(t *testing.T) {
	t.Run("missing directory", func(t *testing.T) {
		code, _, stderr := runStageSignedUnityPackageForTest(t, filepath.Join(t.TempDir(), "absent"), "3.14.0")
		if code == 0 || !strings.Contains(stderr, "read pack output directory") {
			t.Fatalf("exit code = %d, stderr = %q; want a failure reading the directory", code, stderr)
		}
	})
	t.Run("not gzip", func(t *testing.T) {
		directory := t.TempDir()
		if err := os.WriteFile(filepath.Join(directory, "upm-output.tgz"), []byte("not an archive"), 0o600); err != nil {
			t.Fatalf("write archive: %v", err)
		}
		code, _, stderr := runStageSignedUnityPackageForTest(t, directory, "3.14.0")
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
		code, _, stderr := runStageSignedUnityPackageForTest(t, directory, "3.14.0")
		if code == 0 || !strings.Contains(stderr, "read signed tarball") {
			t.Fatalf("exit code = %d, stderr = %q; want a failure reading the tarball", code, stderr)
		}
	})
}

// Verifies that when packing drops a whole folder, the failure names the total count and lists
// only the first missing files, which is the realistic way a tilde-suffixed folder goes missing.
func TestStageSignedUnityPackageSummarizesManyMissingFiles(t *testing.T) {
	directory := t.TempDir()
	writeSignedUnityPackageArchive(t, directory, "upm-output.tgz", validSignedUnityPackageEntries())
	sourceDirectory := writeSignedUnityPackageSource(t)
	skillsDirectory := filepath.Join(sourceDirectory, "Editor", "CliOnlyTools~", "Many")
	if err := os.MkdirAll(skillsDirectory, 0o755); err != nil {
		t.Fatalf("create skills directory: %v", err)
	}
	for index := 0; index < 12; index++ {
		if err := os.WriteFile(filepath.Join(skillsDirectory, fmt.Sprintf("skill-%02d.md", index)), []byte("# skill"), 0o600); err != nil {
			t.Fatalf("write skill: %v", err)
		}
	}

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := RunStageSignedUnityPackage(&stdout, &stderr, []string{"--dir", directory, "--version", "3.14.0", "--source", sourceDirectory})
	if code == 0 {
		t.Fatalf("exit code = 0, stdout = %q; want a failure", stdout.String())
	}
	message := stderr.String()
	if !strings.Contains(message, "12 package source files are missing") {
		t.Fatalf("stderr = %q, want the total count of missing files", message)
	}
	if !strings.Contains(message, "skill-09.md") || strings.Contains(message, "skill-10.md") {
		t.Fatalf("stderr = %q, want only the first 10 missing files listed", message)
	}
}

// Verifies a package source directory that cannot be read fails the staging step, so a broken
// checkout cannot pass the completeness check by having nothing to compare.
func TestStageSignedUnityPackageRejectsUnreadableSource(t *testing.T) {
	directory := t.TempDir()
	writeSignedUnityPackageArchive(t, directory, "upm-output.tgz", validSignedUnityPackageEntries())
	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := RunStageSignedUnityPackage(&stdout, &stderr, []string{"--dir", directory, "--version", "3.14.0", "--source", filepath.Join(t.TempDir(), "absent")})
	if code == 0 || !strings.Contains(stderr.String(), "read package source") {
		t.Fatalf("exit code = %d, stderr = %q; want a failure reading the package source", code, stderr.String())
	}
}

// Verifies missing or unknown arguments exit with the usage status.
func TestRunStageSignedUnityPackageRejectsBadArguments(t *testing.T) {
	for _, args := range [][]string{
		{"--version", "3.14.0", "--source", "source"},
		{"--dir", "signed", "--source", "source"},
		{"--dir", "signed", "--version", "3.14.0"},
		{"--dir", "signed", "--version", "3.14.0", "--source", "source", "--unknown"},
	} {
		var stdout bytes.Buffer
		var stderr bytes.Buffer
		code := RunStageSignedUnityPackage(&stdout, &stderr, args)
		if code != 2 || stdout.Len() != 0 {
			t.Fatalf("args %v: exit code = %d, stdout = %q; want 2 without output", args, code, stdout.String())
		}
	}
}
