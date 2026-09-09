package compilecheck

import (
	"fmt"
	"os"
	"path/filepath"
	"strings"
)

const (
	responseFileExtension      = ".rsp"
	referenceAssemblyExtension = ".ref.dll"

	outputFlagPrefix         = "-out:"
	referenceOutputFlagPref  = "-refout:"
	defineFlagPrefix         = "-define:"
	referenceFlagPrefix      = "-r:"
	analyzerFlagPrefix       = "-analyzer:"
	additionalFileFlagPrefix = "/additionalfile:"
)

// ResponseFile is one Bee-written csc response file, split into the parts compile-check rewrites.
type ResponseFile struct {
	AssemblyName   string // the response file base name without its extension
	Path           string // absolute path of the original response file
	OutputPath     string // -out value, relative to the project root
	RefOutputPath  string // -refout value, relative to the project root
	Defines        []string
	References     []string // -r values, order preserved, duplicates kept
	Analyzers      []string
	Sources        []string // unquoted, relative to the project root
	AdditionalFile string   // /additionalfile value
	OtherFlags     []string // every other flag line, verbatim
}

// ParseResponseFile reads one Bee response file into its parts.
func ParseResponseFile(path string) (ResponseFile, error) {
	content, err := os.ReadFile(path)
	if err != nil {
		return ResponseFile{}, fmt.Errorf("failed to read response file %s: %w", path, err)
	}

	parsed := ResponseFile{
		AssemblyName: strings.TrimSuffix(filepath.Base(path), responseFileExtension),
		Path:         path,
	}
	for _, rawLine := range strings.Split(string(content), "\n") {
		line := strings.TrimRight(rawLine, "\r")
		if line == "" {
			continue
		}
		if err := parsed.appendLine(line); err != nil {
			return ResponseFile{}, fmt.Errorf("%w in %s", err, path)
		}
	}

	if parsed.OutputPath == "" {
		return ResponseFile{}, fmt.Errorf("response file %s has no -out flag", path)
	}
	if len(parsed.Sources) == 0 {
		return ResponseFile{}, fmt.Errorf("response file %s lists no source files", path)
	}

	return parsed, nil
}

// appendLine sorts one response file line into the field it belongs to.
func (r *ResponseFile) appendLine(line string) error {
	switch {
	case strings.HasPrefix(line, outputFlagPrefix):
		r.OutputPath = unquotePath(strings.TrimPrefix(line, outputFlagPrefix))
	case strings.HasPrefix(line, referenceOutputFlagPref):
		r.RefOutputPath = unquotePath(strings.TrimPrefix(line, referenceOutputFlagPref))
	case strings.HasPrefix(line, defineFlagPrefix):
		r.Defines = append(r.Defines, strings.TrimPrefix(line, defineFlagPrefix))
	case strings.HasPrefix(line, referenceFlagPrefix):
		r.References = append(r.References, unquotePath(strings.TrimPrefix(line, referenceFlagPrefix)))
	case strings.HasPrefix(line, analyzerFlagPrefix):
		r.Analyzers = append(r.Analyzers, unquotePath(strings.TrimPrefix(line, analyzerFlagPrefix)))
	case strings.HasPrefix(line, additionalFileFlagPrefix):
		r.AdditionalFile = unquotePath(strings.TrimPrefix(line, additionalFileFlagPrefix))
	case line[0] == '"':
		r.Sources = append(r.Sources, unquotePath(line))
	case line[0] == '-' || line[0] == '/':
		r.OtherFlags = append(r.OtherFlags, line)
	default:
		return fmt.Errorf("unrecognized response file line %q", line)
	}

	return nil
}

// ProjectAssemblyReferences names the other project assemblies this one references by reference
// assembly. dagDir must be spelled the way the response file spells it, which is relative to the
// project root: Bee writes every project-local path relative to the root the compiler runs from.
func (r ResponseFile) ProjectAssemblyReferences(dagDir string) []string {
	names := make([]string, 0, len(r.References))
	for _, reference := range r.References {
		name, ok := projectAssemblyReferenceName(reference, dagDir)
		if ok {
			names = append(names, name)
		}
	}

	return names
}

// projectAssemblyReferenceName reads the assembly name out of a "<dagDir>/<name>.ref.dll" reference.
// Why the separators are normalized: the response file and the caller's dag directory can spell the
// same path with different separators on Windows, and a raw comparison would silently match nothing.
func projectAssemblyReferenceName(reference string, dagDir string) (string, bool) {
	normalizedReference := filepath.ToSlash(reference)
	prefix := strings.TrimSuffix(filepath.ToSlash(dagDir), "/") + "/"
	if !strings.HasPrefix(normalizedReference, prefix) {
		return "", false
	}

	name := strings.TrimPrefix(normalizedReference, prefix)
	if !strings.HasSuffix(name, referenceAssemblyExtension) || strings.Contains(name, "/") {
		return "", false
	}

	return strings.TrimSuffix(name, referenceAssemblyExtension), true
}

// unquotePath strips the one pair of quotes Bee writes around a path and adapts its separators.
func unquotePath(value string) string {
	unquoted := strings.TrimSuffix(strings.TrimPrefix(value, `"`), `"`)

	return filepath.FromSlash(unquoted)
}
