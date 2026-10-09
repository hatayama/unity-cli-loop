package automation

import (
	"archive/tar"
	"compress/gzip"
	"encoding/json"
	"errors"
	"flag"
	"fmt"
	"io"
	"io/fs"
	"os"
	"path/filepath"
	"sort"
	"strings"
)

const (
	stageSignedUnityPackageCommandName = "stage-signed-unity-package"
	signedUnityPackageManifestEntry    = "package/package.json"
	signedUnityPackageSignatureEntry   = "package/.attestation.p7m"
	signedUnityPackageEntryPrefix      = "package/"
	// signedUnityPackageMissingReportLimit keeps the failure message readable when packing drops a
	// whole folder.
	signedUnityPackageMissingReportLimit = 10
	// signedUnityPackageManifestLimit bounds the package.json read so a corrupt entry header
	// cannot make the check allocate an arbitrary amount of memory.
	signedUnityPackageManifestLimit = 1 << 20
)

type signedUnityPackageStageOptions struct {
	Directory string
	Version   string
	Source    string
}

// signedUnityPackageContents is what the staging check learns from one pass over the tarball.
type signedUnityPackageContents struct {
	manifest       []byte
	hasManifest    bool
	signatureSize  int64
	regularEntries map[string]bool
}

type signedUnityPackageManifest struct {
	Name    string `json:"name"`
	Version string `json:"version"`
}

// RunStageSignedUnityPackage checks the single tarball `upm pack` wrote into --dir and renames it
// to the release asset name OpenUPM picks up, printing archive=<path> as a GITHUB_OUTPUT line.
// The tarball is rejected unless it is signed, is this package at --version, and contains every
// file of the package source in --source, because OpenUPM republishes it without any rebuild.
func RunStageSignedUnityPackage(stdout io.Writer, stderr io.Writer, args []string) int {
	options, err := parseSignedUnityPackageStageOptions(args)
	if err != nil {
		_, _ = fmt.Fprintln(stderr, stageSignedUnityPackageCommandName+":", err)
		return 2
	}
	stagedPath, err := stageSignedUnityPackage(options)
	if err != nil {
		_, _ = fmt.Fprintln(stderr, stageSignedUnityPackageCommandName+":", err)
		return 1
	}
	_, _ = fmt.Fprintf(stdout, "archive=%s\n", stagedPath)
	return 0
}

func parseSignedUnityPackageStageOptions(args []string) (signedUnityPackageStageOptions, error) {
	flags := flag.NewFlagSet(stageSignedUnityPackageCommandName, flag.ContinueOnError)
	flags.SetOutput(io.Discard)
	directory := flags.String("dir", "", "directory upm pack wrote the signed tarball into")
	version := flags.String("version", "", "package version the release tag names")
	source := flags.String("source", "", "package source directory the tarball was packed from")
	if err := flags.Parse(args); err != nil {
		return signedUnityPackageStageOptions{}, err
	}
	if *directory == "" {
		return signedUnityPackageStageOptions{}, errors.New("--dir is required")
	}
	if *version == "" {
		return signedUnityPackageStageOptions{}, errors.New("--version is required")
	}
	if *source == "" {
		return signedUnityPackageStageOptions{}, errors.New("--source is required")
	}
	return signedUnityPackageStageOptions{Directory: *directory, Version: *version, Source: *source}, nil
}

func stageSignedUnityPackage(options signedUnityPackageStageOptions) (string, error) {
	archivePath, err := findSingleSignedUnityPackageArchive(options.Directory)
	if err != nil {
		return "", err
	}
	contents, err := readSignedUnityPackageContents(archivePath)
	if err != nil {
		return "", err
	}
	if err := contents.validate(options.Version); err != nil {
		return "", fmt.Errorf("%s: %w", filepath.Base(archivePath), err)
	}
	if err := contents.requireSourceFiles(options.Source); err != nil {
		return "", fmt.Errorf("%s: %w", filepath.Base(archivePath), err)
	}

	stagedPath := filepath.Join(options.Directory, signedUnityPackageAssetName(options.Version))
	if err := os.Rename(archivePath, stagedPath); err != nil {
		return "", fmt.Errorf("rename signed tarball: %w", err)
	}
	return stagedPath, nil
}

func findSingleSignedUnityPackageArchive(directory string) (string, error) {
	entries, err := os.ReadDir(directory)
	if err != nil {
		return "", fmt.Errorf("read pack output directory: %w", err)
	}
	var archives []string
	for _, entry := range entries {
		name := entry.Name()
		if entry.Type().IsRegular() && (strings.HasSuffix(name, ".tgz") || strings.HasSuffix(name, ".tar.gz")) {
			archives = append(archives, filepath.Join(directory, name))
		}
	}
	if len(archives) != 1 {
		return "", fmt.Errorf("expected exactly one signed tarball in %s, found %d", directory, len(archives))
	}
	return archives[0], nil
}

func readSignedUnityPackageContents(archivePath string) (signedUnityPackageContents, error) {
	file, err := os.Open(archivePath)
	if err != nil {
		return signedUnityPackageContents{}, fmt.Errorf("open signed tarball: %w", err)
	}
	defer func() {
		_ = file.Close()
	}()
	gzipReader, err := gzip.NewReader(file)
	if err != nil {
		return signedUnityPackageContents{}, fmt.Errorf("read signed tarball: %w", err)
	}

	contents := signedUnityPackageContents{regularEntries: map[string]bool{}}
	tarReader := tar.NewReader(gzipReader)
	for {
		header, err := tarReader.Next()
		if errors.Is(err, io.EOF) {
			return contents, nil
		}
		if err != nil {
			return signedUnityPackageContents{}, fmt.Errorf("read signed tarball: %w", err)
		}
		if err := contents.record(header, tarReader); err != nil {
			return signedUnityPackageContents{}, err
		}
	}
}

func (contents *signedUnityPackageContents) record(header *tar.Header, entryReader io.Reader) error {
	name := header.Name
	if header.Typeflag == tar.TypeReg {
		contents.regularEntries[name] = true
	}
	switch name {
	case signedUnityPackageSignatureEntry:
		contents.signatureSize = header.Size
	case signedUnityPackageManifestEntry:
		manifest, err := io.ReadAll(io.LimitReader(entryReader, signedUnityPackageManifestLimit))
		if err != nil {
			return fmt.Errorf("read %s: %w", signedUnityPackageManifestEntry, err)
		}
		contents.manifest = manifest
		contents.hasManifest = true
	}
	return nil
}

func (contents signedUnityPackageContents) validate(version string) error {
	if contents.signatureSize <= 0 {
		return fmt.Errorf("%s is missing or empty, so the tarball is not signed", signedUnityPackageSignatureEntry)
	}
	if !contents.hasManifest {
		return fmt.Errorf("%s is missing", signedUnityPackageManifestEntry)
	}
	var manifest signedUnityPackageManifest
	if err := json.Unmarshal(contents.manifest, &manifest); err != nil {
		return fmt.Errorf("parse %s: %w", signedUnityPackageManifestEntry, err)
	}
	if manifest.Name != unityPackageName || manifest.Version != version {
		return fmt.Errorf("%s names %s@%s, want %s@%s", signedUnityPackageManifestEntry, manifest.Name, manifest.Version, unityPackageName, version)
	}
	return nil
}

// requireSourceFiles fails when any regular file of the package source is not a regular file in
// the tarball. A dropped asset .meta file makes Unity import the asset with a new GUID, and a
// dropped tilde-suffixed folder loses the CLI-only skills, so no file may go missing.
func (contents signedUnityPackageContents) requireSourceFiles(sourceDirectory string) error {
	var missing []string
	err := filepath.WalkDir(sourceDirectory, func(path string, entry fs.DirEntry, walkErr error) error {
		if walkErr != nil {
			return walkErr
		}
		if !entry.Type().IsRegular() {
			return nil
		}
		relativePath, err := filepath.Rel(sourceDirectory, path)
		if err != nil {
			return err
		}
		// Tar entry names always use "/", while a Windows walk yields backslash-separated paths.
		packedName := signedUnityPackageEntryPrefix + filepath.ToSlash(relativePath)
		if !contents.regularEntries[packedName] {
			missing = append(missing, packedName)
		}
		return nil
	})
	if err != nil {
		return fmt.Errorf("read package source: %w", err)
	}
	if len(missing) == 0 {
		return nil
	}
	sort.Strings(missing)
	reported := missing
	if len(reported) > signedUnityPackageMissingReportLimit {
		reported = reported[:signedUnityPackageMissingReportLimit]
	}
	return fmt.Errorf("%d package source files are missing from the tarball: %s", len(missing), strings.Join(reported, ", "))
}
