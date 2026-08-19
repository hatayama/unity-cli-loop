package dispatcher

import (
	"bytes"
	"encoding/json"
	"fmt"
)

const (
	openUPMRegistryURL  = "https://package.openupm.com"
	openUPMRegistryName = "package.openupm.com"
)

// packageManifestMergeResult reports how Packages/manifest.json changed.
type packageManifestMergeResult struct {
	Content         []byte
	Changed         bool
	RegistryAdded   bool
	ScopeAdded      bool
	DependencyAdded bool
	PreviousVersion string
}

func mergePackageManifest(content []byte, version string) (packageManifestMergeResult, error) {
	hadCRLF := bytes.Contains(content, []byte("\r\n"))
	hadTrailingNewline := len(content) > 0 && (content[len(content)-1] == '\n')
	normalized := bytes.ReplaceAll(content, []byte("\r\n"), []byte("\n"))

	root, err := parseOrderedJSONObjectBytes(normalized)
	if err != nil {
		return packageManifestMergeResult{}, err
	}

	result := packageManifestMergeResult{}
	if err := updatePackageManifestDependencies(&root, version, &result); err != nil {
		return packageManifestMergeResult{}, err
	}
	if err := updatePackageManifestScopedRegistries(&root, &result); err != nil {
		return packageManifestMergeResult{}, err
	}

	if !result.Changed {
		result.Content = append([]byte{}, content...)
		return result, nil
	}

	emitted, err := emitOrderedJSONObject(root, 0)
	if err != nil {
		return packageManifestMergeResult{}, err
	}
	if hadCRLF {
		emitted = bytes.ReplaceAll(emitted, []byte("\n"), []byte("\r\n"))
	}
	if hadTrailingNewline {
		if hadCRLF {
			if !bytes.HasSuffix(emitted, []byte("\r\n")) {
				emitted = append(emitted, '\r', '\n')
			}
		} else if !bytes.HasSuffix(emitted, []byte("\n")) {
			emitted = append(emitted, '\n')
		}
	} else {
		emitted = bytes.TrimSuffix(emitted, []byte("\r\n"))
		emitted = bytes.TrimSuffix(emitted, []byte("\n"))
	}
	result.Content = emitted
	return result, nil
}

func updatePackageManifestDependencies(root *orderedJSONObject, version string, result *packageManifestMergeResult) error {
	rawDependencies, ok := root.values["dependencies"]
	if !ok {
		dependencies := orderedJSONObject{
			keys:   []string{},
			values: map[string]json.RawMessage{},
		}
		insertDependencyAlphabetically(&dependencies, dispatcherUnityPackageName, version)
		encoded, err := emitOrderedJSONObject(dependencies, 1)
		if err != nil {
			return err
		}
		root.insertAfter("dependencies", encoded, "")
		result.Changed = true
		result.DependencyAdded = true
		return nil
	}

	dependencies, err := parseOrderedJSONObjectBytes(rawDependencies)
	if err != nil {
		return fmt.Errorf("dependencies: %w", err)
	}

	versionJSON, err := json.Marshal(version)
	if err != nil {
		return err
	}
	existing, exists := dependencies.values[dispatcherUnityPackageName]
	if exists {
		var previous string
		if err := json.Unmarshal(existing, &previous); err != nil {
			return fmt.Errorf("dependency %s: %w", dispatcherUnityPackageName, err)
		}
		if previous == version {
			return nil
		}
		dependencies.values[dispatcherUnityPackageName] = json.RawMessage(versionJSON)
		result.Changed = true
		result.PreviousVersion = previous
	} else {
		insertDependencyAlphabetically(&dependencies, dispatcherUnityPackageName, version)
		result.Changed = true
		result.DependencyAdded = true
	}

	encoded, err := emitOrderedJSONObject(dependencies, 1)
	if err != nil {
		return err
	}
	root.values["dependencies"] = encoded
	return nil
}

func insertDependencyAlphabetically(dependencies *orderedJSONObject, name string, version string) {
	versionJSON, err := json.Marshal(version)
	if err != nil {
		panic(err)
	}
	insertAt := len(dependencies.keys)
	for index, key := range dependencies.keys {
		if key > name {
			insertAt = index
			break
		}
	}
	dependencies.keys = append(dependencies.keys, "")
	copy(dependencies.keys[insertAt+1:], dependencies.keys[insertAt:])
	dependencies.keys[insertAt] = name
	if dependencies.values == nil {
		dependencies.values = map[string]json.RawMessage{}
	}
	dependencies.values[name] = json.RawMessage(versionJSON)
}

func updatePackageManifestScopedRegistries(root *orderedJSONObject, result *packageManifestMergeResult) error {
	rawRegistries, hasRegistries := root.values["scopedRegistries"]
	if !hasRegistries {
		return insertDefaultOpenUPMScopedRegistries(root, result)
	}

	elements, err := parseJSONRawArray(rawRegistries)
	if err != nil {
		return fmt.Errorf("scopedRegistries: %w", err)
	}

	openUPMIndex, parsedEntries, err := locateOpenUPMScopedRegistry(elements)
	if err != nil {
		return err
	}
	if openUPMIndex < 0 {
		return appendOpenUPMScopedRegistry(root, elements, result)
	}
	return addMissingOpenUPMScopeToRegistry(root, elements, parsedEntries[openUPMIndex], openUPMIndex, result)
}

func insertDefaultOpenUPMScopedRegistries(root *orderedJSONObject, result *packageManifestMergeResult) error {
	encoded, err := emitOpenUPMScopedRegistriesArray(1)
	if err != nil {
		return err
	}
	root.insertAfter("scopedRegistries", encoded, "dependencies")
	result.Changed = true
	result.RegistryAdded = true
	result.ScopeAdded = true
	return nil
}

func locateOpenUPMScopedRegistry(elements []json.RawMessage) (int, []orderedJSONObject, error) {
	openUPMIndex := -1
	parsedEntries := make([]orderedJSONObject, len(elements))
	for index, element := range elements {
		entry, parseErr := parseOrderedJSONObjectBytes(element)
		if parseErr != nil {
			return -1, nil, fmt.Errorf("scopedRegistries[%d]: %w", index, parseErr)
		}
		parsedEntries[index] = entry
		matched, matchErr := isOpenUPMRegistryEntry(entry, index)
		if matchErr != nil {
			return -1, nil, matchErr
		}
		if matched {
			openUPMIndex = index
		}
	}
	return openUPMIndex, parsedEntries, nil
}

func isOpenUPMRegistryEntry(entry orderedJSONObject, index int) (bool, error) {
	urlRaw, ok := entry.values["url"]
	if !ok {
		return false, nil
	}
	url := ""
	if err := json.Unmarshal(urlRaw, &url); err != nil {
		return false, fmt.Errorf("scopedRegistries[%d].url: %w", index, err)
	}
	return url == openUPMRegistryURL, nil
}

func appendOpenUPMScopedRegistry(root *orderedJSONObject, elements []json.RawMessage, result *packageManifestMergeResult) error {
	newEntry, err := buildOpenUPMRegistryEntry()
	if err != nil {
		return err
	}
	elements = append(elements, newEntry)
	encoded, err := emitJSONRawArray(elements, 1)
	if err != nil {
		return err
	}
	root.values["scopedRegistries"] = encoded
	result.Changed = true
	result.RegistryAdded = true
	result.ScopeAdded = true
	return nil
}

func addMissingOpenUPMScopeToRegistry(
	root *orderedJSONObject,
	elements []json.RawMessage,
	entry orderedJSONObject,
	openUPMIndex int,
	result *packageManifestMergeResult,
) error {
	scopesChanged, err := ensureOpenUPMScope(&entry)
	if err != nil {
		return err
	}
	if !scopesChanged {
		return nil
	}
	encodedEntry, err := emitOrderedJSONObject(entry, 2)
	if err != nil {
		return err
	}
	elements[openUPMIndex] = encodedEntry
	encoded, err := emitJSONRawArray(elements, 1)
	if err != nil {
		return err
	}
	root.values["scopedRegistries"] = encoded
	result.Changed = true
	result.ScopeAdded = true
	return nil
}

func ensureOpenUPMScope(entry *orderedJSONObject) (bool, error) {
	rawScopes, ok := entry.values["scopes"]
	if !ok {
		scopesJSON, err := json.Marshal([]string{dispatcherUnityPackageName})
		if err != nil {
			return false, err
		}
		entry.values["scopes"] = json.RawMessage(scopesJSON)
		if !entry.hasKey("scopes") {
			entry.keys = append(entry.keys, "scopes")
		}
		return true, nil
	}

	var scopes []string
	if err := json.Unmarshal(rawScopes, &scopes); err != nil {
		return false, fmt.Errorf("scopes: %w", err)
	}
	for _, scope := range scopes {
		if scope == dispatcherUnityPackageName {
			return false, nil
		}
	}
	scopes = append(scopes, dispatcherUnityPackageName)
	sortStringsByteOrder(scopes)
	scopesJSON, err := json.Marshal(scopes)
	if err != nil {
		return false, err
	}
	entry.values["scopes"] = json.RawMessage(scopesJSON)
	return true, nil
}

func buildOpenUPMRegistryEntry() (json.RawMessage, error) {
	entry := orderedJSONObject{
		keys: []string{"name", "url", "scopes"},
		values: map[string]json.RawMessage{
			"name":   mustMarshalJSON(openUPMRegistryName),
			"url":    mustMarshalJSON(openUPMRegistryURL),
			"scopes": mustMarshalJSON([]string{dispatcherUnityPackageName}),
		},
	}
	return emitOrderedJSONObject(entry, 2)
}

func emitOpenUPMScopedRegistriesArray(depth int) (json.RawMessage, error) {
	entry, err := buildOpenUPMRegistryEntry()
	if err != nil {
		return nil, err
	}
	return emitJSONRawArray([]json.RawMessage{entry}, depth)
}
