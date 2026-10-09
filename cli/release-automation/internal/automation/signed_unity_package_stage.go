package automation

import (
	"archive/tar"
	"compress/gzip"
	"encoding/json"
	"errors"
	"flag"
	"fmt"
	"io"
	"os"
	"path/filepath"
	"strings"
)

const (
	stageSignedUnityPackageCommandName = "stage-signed-unity-package"
	signedUnityPackageManifestEntry    = "package/package.json"
	signedUnityPackageSignatureEntry   = "package/.attestation.p7m"
	signedUnityPackageCliSkillsPrefix  = "package/Editor/CliOnlyTools~/"
	// signedUnityPackageManifestLimit bounds the package.json read so a corrupt entry header
	// cannot make the check allocate an arbitrary amount of memory.
	signedUnityPackageManifestLimit = 1 << 20
)

type signedUnityPackageStageOptions struct {
	Directory string
	Version   string
}

// signedUnityPackageContents is what the staging check learns from one pass over the tarball.
type signedUnityPackageContents struct {
	manifest      []byte
	hasManifest   bool
	signatureSize int64
	hasMetaFile   bool
	hasCliSkills  bool
}

type signedUnityPackageManifest struct {
	Name    string `json:"name"`
	Version string `json:"version"`
}

// RunStageSignedUnityPackage checks the single tarball `upm pack` wrote into --dir and renames it
// to the release asset name OpenUPM picks up, printing archive=<path> as a GITHUB_OUTPUT line.
// The tarball is rejected unless it is signed, is this package at --version, and still carries the
// Unity .meta files and the CLI-only skills, because OpenUPM republishes it without any rebuild.
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
	if err := flags.Parse(args); err != nil {
		return signedUnityPackageStageOptions{}, err
	}
	if *directory == "" {
		return signedUnityPackageStageOptions{}, errors.New("--dir is required")
	}
	if *version == "" {
		return signedUnityPackageStageOptions{}, errors.New("--version is required")
	}
	return signedUnityPackageStageOptions{Directory: *directory, Version: *version}, nil
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

	contents := signedUnityPackageContents{}
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
	if strings.HasPrefix(name, signedUnityPackageCliSkillsPrefix) {
		contents.hasCliSkills = true
	}
	if strings.HasSuffix(name, ".meta") {
		contents.hasMetaFile = true
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
	if !contents.hasMetaFile {
		return errors.New("no .meta files were packed, so Unity would reimport the package with new GUIDs")
	}
	if !contents.hasCliSkills {
		return fmt.Errorf("no entries under %s were packed, so the CLI-only skills would be missing", signedUnityPackageCliSkillsPrefix)
	}
	return nil
}
