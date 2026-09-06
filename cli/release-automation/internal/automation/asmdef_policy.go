package automation

import (
	"encoding/json"
	"fmt"
	"io"
	"io/fs"
	"os"
	"path/filepath"
	"regexp"
	"sort"
	"strings"
)

// asmdefSourceRoot is the only tree the policy checker walks. Test assemblies
// under Assets/Tests legitimately reference several tools at once, so they stay
// out of scope by omission.
const asmdefSourceRoot = "Packages/src"

// DefaultAsmdefPolicyAllowlistPath is where exemptions live, relative to the
// repository root. Every entry names one (from, to) reference and the reason it
// is tolerated for now.
const DefaultAsmdefPolicyAllowlistPath = "tools/asmdef-policy-allowlist.json"

// asmdefGUIDReferencePrefix marks a reference that names its target by asset
// GUID rather than by assembly name.
const asmdefGUIDReferencePrefix = "GUID:"

// asmdefMetaGUIDPattern matches the guid line of a .asmdef.meta file. The
// trailing whitespace class tolerates CRLF line endings on Windows checkouts.
var asmdefMetaGUIDPattern = regexp.MustCompile(`(?m)^guid: ([0-9a-f]{32})\s*$`)

// AsmdefAssembly is one assembly definition with its references resolved to
// assembly names. References to assemblies outside Packages/src are dropped.
type AsmdefAssembly struct {
	Name       string
	Path       string
	References []string
}

// AsmdefPolicyFinding is one reference that the policy forbids.
type AsmdefPolicyFinding struct {
	From string
	To   string
	Rule string
	Path string
}

// AsmdefPolicyCheckOptions configures RunAsmdefPolicyCheck.
type AsmdefPolicyCheckOptions struct {
	Root          string
	AllowlistPath string
}

type asmdefAllowedReference struct {
	From   string `json:"from"`
	To     string `json:"to"`
	Reason string `json:"reason"`
}

type asmdefAllowlist struct {
	AllowedReferences []asmdefAllowedReference `json:"allowedReferences"`
}

type asmdefDocument struct {
	Name       string   `json:"name"`
	References []string `json:"references"`
}

type asmdefRawAssembly struct {
	document asmdefDocument
	guid     string
	path     string
}

// LoadAsmdefAssemblies reads every .asmdef under Packages/src and resolves its
// references to assembly names. GUID references are resolved through the
// sibling .asmdef.meta files; references that match no loaded assembly are
// external packages and are dropped.
func LoadAsmdefAssemblies(root string) ([]AsmdefAssembly, error) {
	rawAssemblies, err := readAsmdefFiles(root)
	if err != nil {
		return nil, err
	}
	// A run that found nothing must not pass: with the default --root of ".",
	// running from the wrong directory would otherwise report success without
	// having looked at a single assembly definition.
	if len(rawAssemblies) == 0 {
		return nil, fmt.Errorf("no .asmdef files found under %s; pass --root <repository root>", filepath.Join(root, asmdefSourceRoot))
	}

	nameByGUID := map[string]string{}
	knownNames := map[string]bool{}
	for _, raw := range rawAssemblies {
		nameByGUID[raw.guid] = raw.document.Name
		knownNames[raw.document.Name] = true
	}

	assemblies := make([]AsmdefAssembly, 0, len(rawAssemblies))
	for _, raw := range rawAssemblies {
		assemblies = append(assemblies, AsmdefAssembly{
			Name:       raw.document.Name,
			Path:       raw.path,
			References: resolveAsmdefReferences(raw.document.References, nameByGUID, knownNames),
		})
	}
	sort.Slice(assemblies, func(left int, right int) bool {
		return assemblies[left].Name < assemblies[right].Name
	})
	return assemblies, nil
}

func readAsmdefFiles(root string) ([]asmdefRawAssembly, error) {
	absoluteRoot := filepath.Join(root, filepath.FromSlash(asmdefSourceRoot))
	if _, err := os.Stat(absoluteRoot); err != nil {
		if os.IsNotExist(err) {
			return nil, nil
		}
		return nil, err
	}

	rawAssemblies := []asmdefRawAssembly{}
	walkErr := filepath.WalkDir(absoluteRoot, func(path string, entry fs.DirEntry, walkErr error) error {
		if walkErr != nil {
			return walkErr
		}
		// Unity ignores tilde-suffixed folders and generates no .meta files for
		// them, so an .asmdef inside one is not part of the compiled package.
		if entry.IsDir() && strings.HasSuffix(entry.Name(), "~") {
			return fs.SkipDir
		}
		if entry.IsDir() || !strings.HasSuffix(entry.Name(), ".asmdef") {
			return nil
		}
		raw, err := readAsmdefFile(root, path)
		if err != nil {
			return err
		}
		rawAssemblies = append(rawAssemblies, raw)
		return nil
	})
	if walkErr != nil {
		return nil, walkErr
	}
	return rawAssemblies, nil
}

func readAsmdefFile(root string, path string) (asmdefRawAssembly, error) {
	content, err := os.ReadFile(path)
	if err != nil {
		return asmdefRawAssembly{}, err
	}
	document := asmdefDocument{}
	if err := json.Unmarshal(content, &document); err != nil {
		return asmdefRawAssembly{}, fmt.Errorf("parse %s: %w", path, err)
	}
	if document.Name == "" {
		return asmdefRawAssembly{}, fmt.Errorf("%s has no assembly name", path)
	}
	guid, err := readAsmdefGUID(path + ".meta")
	if err != nil {
		return asmdefRawAssembly{}, err
	}
	relativePath, err := filepath.Rel(root, path)
	if err != nil {
		return asmdefRawAssembly{}, err
	}
	return asmdefRawAssembly{
		document: document,
		guid:     guid,
		path:     filepath.ToSlash(relativePath),
	}, nil
}

func readAsmdefGUID(metaPath string) (string, error) {
	content, err := os.ReadFile(metaPath)
	if err != nil {
		return "", fmt.Errorf("read %s: %w (every .asmdef under Packages/src needs its Unity .meta file)", metaPath, err)
	}
	match := asmdefMetaGUIDPattern.FindSubmatch(content)
	if match == nil {
		return "", fmt.Errorf("%s has no guid line", metaPath)
	}
	return string(match[1]), nil
}

func resolveAsmdefReferences(references []string, nameByGUID map[string]string, knownNames map[string]bool) []string {
	resolved := []string{}
	for _, reference := range references {
		name := reference
		if strings.HasPrefix(reference, asmdefGUIDReferencePrefix) {
			name = nameByGUID[strings.TrimPrefix(reference, asmdefGUIDReferencePrefix)]
		}
		if !knownNames[name] {
			continue
		}
		resolved = append(resolved, name)
	}
	sort.Strings(resolved)
	return resolved
}

func loadAsmdefAllowlist(path string) (asmdefAllowlist, error) {
	content, err := os.ReadFile(path)
	if err != nil {
		return asmdefAllowlist{}, fmt.Errorf("read allowlist %s: %w", path, err)
	}
	allowlist := asmdefAllowlist{}
	if err := json.Unmarshal(content, &allowlist); err != nil {
		return asmdefAllowlist{}, fmt.Errorf("parse allowlist %s: %w", path, err)
	}
	for _, entry := range allowlist.AllowedReferences {
		if entry.From == "" || entry.To == "" || entry.Reason == "" {
			return asmdefAllowlist{}, fmt.Errorf("allowlist %s: every entry needs from, to, and reason", path)
		}
	}
	return allowlist, nil
}

// RunAsmdefPolicyCheck prints findings and returns the process exit code. Any
// reference outside the policy and outside the allowlist fails the check, and
// so does an allowlist entry whose reference no longer exists, so the allowlist
// shrinks as debts are repaid instead of accumulating dead entries.
func RunAsmdefPolicyCheck(stdout io.Writer, stderr io.Writer, options AsmdefPolicyCheckOptions) int {
	assemblies, err := LoadAsmdefAssemblies(options.Root)
	if err != nil {
		_, _ = fmt.Fprintln(stderr, "check-asmdef-policy:", err)
		return 1
	}
	findings, err := evaluateAsmdefPolicy(assemblies)
	if err != nil {
		_, _ = fmt.Fprintln(stderr, "check-asmdef-policy:", err)
		return 1
	}
	allowlistPath := options.AllowlistPath
	if allowlistPath == "" {
		allowlistPath = filepath.Join(options.Root, filepath.FromSlash(DefaultAsmdefPolicyAllowlistPath))
	}
	allowlist, err := loadAsmdefAllowlist(allowlistPath)
	if err != nil {
		_, _ = fmt.Fprintln(stderr, "check-asmdef-policy:", err)
		return 1
	}
	remaining, stale := applyAsmdefAllowlist(findings, allowlist)

	_, _ = fmt.Fprintln(stdout, "=== asmdef reference policy ===")
	if len(remaining) == 0 && len(stale) == 0 {
		_, _ = fmt.Fprintln(stdout, "No asmdef reference violated the policy.")
		return 0
	}
	for _, finding := range remaining {
		_, _ = fmt.Fprintf(stdout, "%s -> %s: %s (%s)\n", finding.From, finding.To, finding.Rule, finding.Path)
	}
	if len(remaining) > 0 {
		_, _ = fmt.Fprintf(stdout, "%d asmdef references violate the policy; remove the reference or add it to %s with a reason.\n", len(remaining), DefaultAsmdefPolicyAllowlistPath)
	}
	for _, entry := range stale {
		_, _ = fmt.Fprintf(stdout, "stale allowlist entry: %s -> %s (reference no longer exists; remove it from the allowlist)\n", entry.From, entry.To)
	}
	return 1
}
