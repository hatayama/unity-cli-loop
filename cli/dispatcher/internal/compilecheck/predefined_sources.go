package compilecheck

import (
	"path/filepath"
	"sort"
	"strings"
)

const (
	assemblyCSharp                = "Assembly-CSharp"
	assemblyCSharpFirstpass       = "Assembly-CSharp-firstpass"
	assemblyCSharpEditor          = "Assembly-CSharp-Editor"
	assemblyCSharpEditorFirstpass = "Assembly-CSharp-Editor-firstpass"
	editorPathSegment             = "/editor/"
)

// firstpassPathPrefixes are the folders whose scripts Unity compiles before the rest, written the
// way Unity matches them: lower case, from a '/' put before the project-relative path.
var firstpassPathPrefixes = []string{
	"/assets/plugins/", "/assets/standard assets/", "/assets/pro standard assets/", "/assets/iphone standard assets/",
}

// uncompiledPathPrefixes are the folders under Assets whose scripts Unity imports but never compiles,
// written the same way. Unity's script importer holds this list in native code, out of sight of
// its C# build rules.
var uncompiledPathPrefixes = []string{
	"/assets/streamingassets/", "/assets/webplayertemplates/", "/assets/webgltemplates/", "/assets/plugins/android/",
}

// hasPathPrefix reports whether a project-relative path, compared the way Unity compares it, starts
// with one of the prefixes.
func hasPathPrefix(relativePath string, prefixes []string) bool {
	lower := "/" + strings.ToLower(filepath.ToSlash(relativePath))
	for _, prefix := range prefixes {
		if strings.HasPrefix(lower, prefix) {
			return true
		}
	}

	return false
}

// predefinedAssemblyFor names the assembly Unity compiles a script outside every assembly definition
// into, from its project-relative path. Unity picks the assembly whose path rule matches deepest; an
// "editor" folder anywhere always matches deeper than a first-pass folder, and of the two editor
// assemblies that then tie, the first-pass one comes first.
func predefinedAssemblyFor(relativePath string) string {
	firstpass := hasPathPrefix(relativePath, firstpassPathPrefixes)
	editor := strings.Contains("/"+strings.ToLower(filepath.ToSlash(relativePath)), editorPathSegment)

	switch {
	case firstpass && editor:
		return assemblyCSharpEditorFirstpass
	case firstpass:
		return assemblyCSharpFirstpass
	case editor:
		return assemblyCSharpEditor
	default:
		return assemblyCSharp
	}
}

// globPredefinedSources lists the scripts under Assets that no assembly definition or reference
// owns, by the predefined assembly Unity compiles each into, sorted. Scripts in the folders Unity
// never compiles are left out.
// Why Packages is left out: Unity's first-pass rule only looks at Assets, and where a script in a
// package folder with no assembly definition goes changes between Unity versions.
func globPredefinedSources(projectRoot string, owners assemblyOwnerIndex) (map[string][]string, error) {
	assets := filepath.Join(projectRoot, assetsDirectoryName)
	byAssembly := map[string][]string{}
	// Why an assembly definition in Assets itself ends the search: every script below then belongs to
	// it, and Unity builds no predefined assembly at all.
	if owners.startsAnotherAssembly(assets) {
		return byAssembly, nil
	}

	sources, err := globAssemblySources(projectRoot, assets, owners)
	if err != nil {
		return nil, err
	}
	for _, source := range sources {
		if hasPathPrefix(source, uncompiledPathPrefixes) {
			continue
		}
		name := predefinedAssemblyFor(source)
		byAssembly[name] = append(byAssembly[name], source)
	}
	for _, assemblySources := range byAssembly {
		sort.Strings(assemblySources)
	}

	return byAssembly, nil
}

// detectPredefinedAssemblyTheBuildLacks refuses a script whose predefined assembly the last Unity
// build did not produce, such as the first editor script of a project that had none: the response
// file that would compile it does not exist yet.
func detectPredefinedAssemblyTheBuildLacks(predefined map[string][]string, graph assemblyGraph) error {
	names := make([]string, 0, len(predefined))
	for name := range predefined {
		names = append(names, name)
	}
	sort.Strings(names)
	for _, name := range names {
		if _, built := graph.byName[name]; built {
			continue
		}

		return unityBuildRequired("%s belongs to %s, which the last Unity build did not produce",
			predefined[name][0], name)
	}

	return nil
}

// appendPredefinedSources adds the scripts a predefined assembly gained since the build after the
// ones it recorded, keeping the recorded order and putting the added ones in ordinal order.
func appendPredefinedSources(existing []string, globbed []string) []string {
	recorded := make(map[string]bool, len(existing))
	for _, source := range existing {
		recorded[filepath.ToSlash(source)] = true
	}
	sources := existing
	for _, source := range globbed {
		if !recorded[source] {
			sources = append(sources, source)
		}
	}

	return sources
}
