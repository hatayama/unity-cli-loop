package compilecheck

import (
	"crypto/sha256"
	"encoding/hex"
	"fmt"
	"hash"
	"io"
	"os"
	"path/filepath"
	"sort"
	"strings"
)

// pathValuedFlagNames are the flags whose value names a file the project edits and csc reads: they
// decide which diagnostics come out, so the key follows what is inside them rather than their
// timestamp - a checkout that restores a ruleset writes a new timestamp on the same content.
var pathValuedFlagNames = []string{"ruleset", "additionalfile", "analyzerconfig"}

// untrackedFileInputFlagNames are the flags naming a file csc reads that this key does not follow.
// Why a unit carrying one gets no key at all: reusing its diagnostics would mean reusing them
// across an edit to a file nothing here watches. Flags that only name where csc writes are absent
// from this list, because what a compile writes cannot change what it reports.
var untrackedFileInputFlagNames = []string{
	"keyfile", "win32manifest", "win32res", "win32icon", "resource", "linkresource",
	"embed", "link", "addmodule", "appconfig", "sourcelink",
}

// computeInputKey digests everything that decides what csc reports for one assembly, so that two
// runs agreeing on this key would get the same diagnostics out of the compiler.
// Why an error rather than a key when an input cannot be read: a key built from what happens to be
// readable would match a later run that reads the same subset, and the compile it stands for never
// ran. The caller compiles such a unit instead of reusing anything.
func computeInputKey(
	projectRoot string, paths EditorCompilerPaths, plan BuildPlan, unit CompileUnit,
) (string, error) {
	digest := sha256.New()
	writers := []func() error{
		func() error { return writeCompilerIdentity(digest, paths) },
		func() error { return writeResponseFileIdentity(digest, unit.Assembly) },
		func() error { return writeSourceIdentity(digest, projectRoot, unit.Sources) },
		func() error { return writeReferenceIdentity(digest, projectRoot, plan, unit.Assembly.References) },
		func() error { return writeAnalyzerIdentity(digest, projectRoot, unit.Assembly.Analyzers) },
		func() error { return writeAnalyzerInputIdentity(digest, projectRoot, unit.Assembly) },
	}
	for _, write := range writers {
		if err := write(); err != nil {
			return "", err
		}
	}
	writeOutputIdentity(digest, unit.Assembly)

	return hex.EncodeToString(digest.Sum(nil)), nil
}

// writeCompilerIdentity records which compiler would run: another one reports other diagnostics.
func writeCompilerIdentity(digest hash.Hash, paths EditorCompilerPaths) error {
	for _, path := range []string{paths.CompilerDllPath, paths.DotnetHostPath} {
		if err := writeStatEntry(digest, "compiler", path, path); err != nil {
			return err
		}
	}

	return nil
}

// writeResponseFileIdentity records the response files Bee wrote for this assembly, which carry its
// defines, its analyzer list and the path mapping its metadata is built with.
func writeResponseFileIdentity(digest hash.Hash, rsp ResponseFile) error {
	companionPath := strings.TrimSuffix(rsp.Path, responseFileExtension) + companionFileExtension
	for _, path := range []string{rsp.Path, companionPath} {
		content, err := os.ReadFile(path)
		// Why a missing companion file is no error: Bee writes one only for some assemblies, and the
		// ones it left out are keyed on its absence, which is what an empty digest records.
		if err != nil && !os.IsNotExist(err) {
			return fmt.Errorf("failed to read %s: %w", path, err)
		}
		writeContentEntry(digest, "response", filepath.Base(path), content)
	}

	return nil
}

// writeSourceIdentity records what the assembly compiles, by content and in a fixed order.
// Why the content rather than the modification time: a checkout that puts a file back the way it
// was leaves it newer than the build, and recompiling it would report the diagnostics it already
// reported. The whole source set of the largest assembly hashes in tens of milliseconds.
func writeSourceIdentity(digest hash.Hash, projectRoot string, sources []string) error {
	ordered := append([]string{}, sources...)
	sort.Strings(ordered)
	for _, source := range ordered {
		if err := writeFileContentEntry(digest, "source", source, projectPath(projectRoot, source)); err != nil {
			return err
		}
	}

	return nil
}

// writeReferenceIdentity records the assemblies this one compiles against, resolved the way the
// compile itself resolves them.
// Why the two rules: a reference this run rebuilds is read by content, because recompiling an
// assembly writes the same public surface with a new timestamp whenever the edit stayed inside a
// method body - the very case this cache exists for. Everything else is read by timestamp, which
// moves whenever Unity rebuilt it, and a compile that did not need it costs less than a missed error.
func writeReferenceIdentity(
	digest hash.Hash, projectRoot string, plan BuildPlan, references []string,
) error {
	for _, reference := range references {
		resolved := rewriteReference(projectRoot, plan, reference)
		path := projectPath(projectRoot, resolved)
		if isUnderDirectory(resolved, plan.OutputDir) {
			if err := writeFileContentEntry(digest, "produced-reference", resolved, path); err != nil {
				return err
			}

			continue
		}
		if err := writeStatEntry(digest, "reference", resolved, path); err != nil {
			return err
		}
	}

	return nil
}

// writeAnalyzerIdentity records the analyzer assemblies csc loads, which are shipped rather than
// edited, so their timestamps answer whether they moved.
func writeAnalyzerIdentity(digest hash.Hash, projectRoot string, analyzers []string) error {
	for _, analyzer := range analyzers {
		if err := writeStatEntry(digest, "analyzer", analyzer, projectPath(projectRoot, analyzer)); err != nil {
			return err
		}
	}

	return nil
}

// writeAnalyzerInputIdentity records the files the analyzers read - a ruleset, a banned-symbol list,
// an editorconfig handed over as /analyzerconfig - and refuses a key for a flag naming a file this
// digest does not follow.
func writeAnalyzerInputIdentity(digest hash.Hash, projectRoot string, rsp ResponseFile) error {
	inputs := []string{}
	if rsp.AdditionalFile != "" {
		inputs = append(inputs, rsp.AdditionalFile)
	}
	for _, flag := range rsp.OtherFlags {
		value, isPathValued := pathValuedFlag(flag)
		if isPathValued {
			inputs = append(inputs, value)

			continue
		}
		if untrackedFileInputFlag(flag) {
			return fmt.Errorf("response file flag %q names a file the input key does not follow", flag)
		}
	}

	for _, input := range inputs {
		if err := writeFileContentEntry(
			digest, "analyzer-input", input, projectPath(projectRoot, input)); err != nil {
			return err
		}
	}

	return nil
}

// writeOutputIdentity records what the compile is named, which is part of what it produces.
func writeOutputIdentity(digest hash.Hash, rsp ResponseFile) {
	for _, path := range []string{rsp.OutputPath, rsp.RefOutputPath} {
		fmt.Fprintf(digest, "output\x00%s\n", filepath.Base(path))
	}
}

// pathValuedFlag reads the file path out of a flag whose content belongs in the key.
func pathValuedFlag(line string) (string, bool) {
	name, value, ok := splitFlag(line)
	if !ok {
		return "", false
	}
	for _, candidate := range pathValuedFlagNames {
		if name == candidate {
			return unquotePath(value), true
		}
	}

	return "", false
}

// untrackedFileInputFlag reports whether a flag names a file csc reads that the key does not follow.
func untrackedFileInputFlag(line string) bool {
	name, _, ok := splitFlag(line)
	if !ok {
		return false
	}
	for _, candidate := range untrackedFileInputFlagNames {
		if name == candidate {
			return true
		}
	}

	return false
}

// splitFlag reads one response file line as a flag name and its value.
// Why the name is lowered and both flag markers are accepted: csc treats -ruleset:, /ruleset: and
// /RuleSet: as the same flag, and a comparison that did not would silently follow none of them.
func splitFlag(line string) (string, string, bool) {
	if line == "" || (line[0] != '-' && line[0] != '/') {
		return "", "", false
	}
	name, value, found := strings.Cut(line[1:], ":")
	if !found {
		return "", "", false
	}

	return strings.ToLower(name), value, true
}

// writeStatEntry records a file by where it is, how big it is and when it last changed.
func writeStatEntry(digest hash.Hash, kind string, name string, path string) error {
	info, err := os.Stat(path)
	if err != nil {
		return fmt.Errorf("failed to read %s: %w", path, err)
	}
	fmt.Fprintf(digest, "%s\x00%s\x00%d\x00%d\n",
		kind, filepath.ToSlash(name), info.Size(), info.ModTime().UnixNano())

	return nil
}

// writeFileContentEntry records a file by where it is and what it holds.
func writeFileContentEntry(digest hash.Hash, kind string, name string, path string) error {
	file, err := os.Open(path)
	if err != nil {
		return fmt.Errorf("failed to read %s: %w", path, err)
	}
	defer func() { _ = file.Close() }()

	content := sha256.New()
	if _, err := io.Copy(content, file); err != nil {
		return fmt.Errorf("failed to read %s: %w", path, err)
	}
	fmt.Fprintf(digest, "%s\x00%s\x00%s\n",
		kind, filepath.ToSlash(name), hex.EncodeToString(content.Sum(nil)))

	return nil
}

// writeContentEntry records bytes already in hand under a name.
func writeContentEntry(digest hash.Hash, kind string, name string, content []byte) {
	sum := sha256.Sum256(content)
	fmt.Fprintf(digest, "%s\x00%s\x00%s\n", kind, filepath.ToSlash(name), hex.EncodeToString(sum[:]))
}

// projectPath resolves a path a response file spells relative to the project root.
func projectPath(projectRoot string, path string) string {
	if filepath.IsAbs(path) {
		return path
	}

	return filepath.Join(projectRoot, path)
}
