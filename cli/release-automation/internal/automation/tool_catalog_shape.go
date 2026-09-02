package automation

import (
	"crypto/sha256"
	"encoding/hex"
	"encoding/json"
	"fmt"
)

// ToolCatalogShapeDigest returns a SHA-256 hex digest of the catalog with every
// tool-level and property-level "description" removed, so description-only
// regenerations produce the same digest. Unknown fields stay in the map so a
// typed unmarshal cannot hide a new structural key.
func ToolCatalogShapeDigest(content []byte) (string, error) {
	canonical, err := canonicalizeToolCatalogShape(content)
	if err != nil {
		return "", err
	}
	sum := sha256.Sum256(canonical)
	return hex.EncodeToString(sum[:]), nil
}

// ToolCatalogShapeChanged reports whether base and head differ in anything
// other than descriptions.
func ToolCatalogShapeChanged(base []byte, head []byte) (bool, error) {
	baseDigest, err := ToolCatalogShapeDigest(base)
	if err != nil {
		return false, err
	}
	headDigest, err := ToolCatalogShapeDigest(head)
	if err != nil {
		return false, err
	}
	return baseDigest != headDigest, nil
}

func canonicalizeToolCatalogShape(content []byte) ([]byte, error) {
	var root map[string]any
	if err := json.Unmarshal(content, &root); err != nil {
		return nil, fmt.Errorf("invalid tool catalog JSON: %w", err)
	}

	stripToolCatalogDescriptions(root)

	canonical, err := json.Marshal(root)
	if err != nil {
		return nil, fmt.Errorf("failed to marshal tool catalog shape: %w", err)
	}
	return canonical, nil
}

func stripToolCatalogDescriptions(root map[string]any) {
	tools, ok := root["tools"].([]any)
	if !ok {
		return
	}
	for _, entry := range tools {
		tool, ok := entry.(map[string]any)
		if !ok {
			continue
		}
		delete(tool, "description")
		schema, ok := tool["inputSchema"].(map[string]any)
		if !ok {
			continue
		}
		props, ok := schema["properties"].(map[string]any)
		if !ok {
			continue
		}
		for _, propEntry := range props {
			prop, ok := propEntry.(map[string]any)
			if !ok {
				continue
			}
			delete(prop, "description")
		}
	}
}
