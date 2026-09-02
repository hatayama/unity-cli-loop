package automation

import (
	"strings"
	"testing"
)

const catalogShapeBase = `{
  "tools": [
    {
      "name": "compile",
      "description": "Compile the project",
      "inputSchema": {
        "type": "object",
        "properties": {
          "X": {
            "type": "string",
            "description": "file path",
            "default": "",
            "enum": ["a", "b"],
            "hidden": false
          }
        }
      }
    }
  ]
}`

// Verifies identical catalog bytes produce the same shape digest.
func TestToolCatalogShapeDigestSameContent(t *testing.T) {
	first, err := ToolCatalogShapeDigest([]byte(catalogShapeBase))
	if err != nil {
		t.Fatalf("first digest failed: %v", err)
	}
	second, err := ToolCatalogShapeDigest([]byte(catalogShapeBase))
	if err != nil {
		t.Fatalf("second digest failed: %v", err)
	}
	if first != second {
		t.Fatalf("expected identical content to share a digest, got %q and %q", first, second)
	}
}

// Verifies changing only a tool-level description keeps the shape digest.
func TestToolCatalogShapeDigestIgnoresToolDescription(t *testing.T) {
	assertSameShapeDigest(t, catalogShapeBase, strings.Replace(catalogShapeBase, "Compile the project", "Compile now", 1))
}

// Verifies changing only a property-level description keeps the shape digest.
func TestToolCatalogShapeDigestIgnoresPropertyDescription(t *testing.T) {
	assertSameShapeDigest(t, catalogShapeBase, strings.Replace(catalogShapeBase, "file path", "target path", 1))
}

// Verifies adding a property changes the shape digest.
func TestToolCatalogShapeDigestDetectsAddedProperty(t *testing.T) {
	changed := `{
  "tools": [
    {
      "name": "compile",
      "description": "Compile the project",
      "inputSchema": {
        "type": "object",
        "properties": {
          "X": {
            "type": "string",
            "description": "file path",
            "default": "",
            "enum": ["a", "b"],
            "hidden": false
          },
          "Y": { "type": "boolean" }
        }
      }
    }
  ]
}`
	assertDifferentShapeDigest(t, catalogShapeBase, changed)
}

// Verifies changing a property type changes the shape digest.
func TestToolCatalogShapeDigestDetectsPropertyTypeChange(t *testing.T) {
	assertDifferentShapeDigest(t, catalogShapeBase, strings.Replace(catalogShapeBase, `"type": "string"`, `"type": "integer"`, 1))
}

// Verifies changing a property default changes the shape digest.
func TestToolCatalogShapeDigestDetectsPropertyDefaultChange(t *testing.T) {
	assertDifferentShapeDigest(t, catalogShapeBase, strings.Replace(catalogShapeBase, `"default": ""`, `"default": "x"`, 1))
}

// Verifies changing a property enum changes the shape digest.
func TestToolCatalogShapeDigestDetectsPropertyEnumChange(t *testing.T) {
	assertDifferentShapeDigest(t, catalogShapeBase, strings.Replace(catalogShapeBase, `"enum": ["a", "b"]`, `"enum": ["a", "c"]`, 1))
}

// Verifies changing a property hidden flag changes the shape digest.
func TestToolCatalogShapeDigestDetectsPropertyHiddenChange(t *testing.T) {
	assertDifferentShapeDigest(t, catalogShapeBase, strings.Replace(catalogShapeBase, `"hidden": false`, `"hidden": true`, 1))
}

// Verifies adding a tool changes the shape digest.
func TestToolCatalogShapeDigestDetectsAddedTool(t *testing.T) {
	changed := `{
  "tools": [
    {
      "name": "compile",
      "description": "Compile the project",
      "inputSchema": {
        "type": "object",
        "properties": {
          "X": {
            "type": "string",
            "description": "file path",
            "default": "",
            "enum": ["a", "b"],
            "hidden": false
          }
        }
      }
    },
    { "name": "screenshot" }
  ]
}`
	assertDifferentShapeDigest(t, catalogShapeBase, changed)
}

// Verifies an unknown top-level field changes the shape digest.
func TestToolCatalogShapeDigestDetectsUnknownTopLevelField(t *testing.T) {
	changed := `{
  "schemaVersion": 2,
  "tools": [
    {
      "name": "compile",
      "description": "Compile the project",
      "inputSchema": {
        "type": "object",
        "properties": {
          "X": {
            "type": "string",
            "description": "file path",
            "default": "",
            "enum": ["a", "b"],
            "hidden": false
          }
        }
      }
    }
  ]
}`
	assertDifferentShapeDigest(t, catalogShapeBase, changed)
}

// Verifies defaults that collapse under float64 still produce different shape digests.
func TestToolCatalogShapeDigestPreservesIntegersBeyondFloat64ExactRange(t *testing.T) {
	left := `{"tools":[{"name":"compile","inputSchema":{"type":"object","properties":{"X":{"type":"integer","default":9007199254740992}}}}]}`
	right := `{"tools":[{"name":"compile","inputSchema":{"type":"object","properties":{"X":{"type":"integer","default":9007199254740993}}}}]}`
	assertDifferentShapeDigest(t, left, right)
}

// Verifies invalid catalog JSON is an error rather than a silent shape change.
func TestToolCatalogShapeDigestRejectsInvalidJSON(t *testing.T) {
	_, err := ToolCatalogShapeDigest([]byte(`{"tools":[`))
	if err == nil {
		t.Fatal("expected invalid JSON to return an error")
	}
}

// Verifies trailing text after the catalog object is rejected instead of being hashed as a valid shape.
func TestToolCatalogShapeDigestRejectsTrailingText(t *testing.T) {
	_, err := ToolCatalogShapeDigest([]byte(`{"tools":[]} garbage`))
	if err == nil {
		t.Fatal("expected trailing text to return an error")
	}
}

// Verifies a second JSON value after the catalog object is rejected instead of being silently ignored.
func TestToolCatalogShapeDigestRejectsSecondJSONValue(t *testing.T) {
	_, err := ToolCatalogShapeDigest([]byte(`{"tools":[]} {"tools":[{"name":"compile"}]}`))
	if err == nil {
		t.Fatal("expected a second JSON value to return an error")
	}
}

// Verifies a description-only edit mixed with an added property is reported as a shape change.
func TestToolCatalogShapeChangedDetectsMixedDescriptionAndPropertyAdd(t *testing.T) {
	head := `{
  "tools": [
    {
      "name": "compile",
      "description": "Compile now",
      "inputSchema": {
        "type": "object",
        "properties": {
          "X": {
            "type": "string",
            "description": "file path",
            "default": "",
            "enum": ["a", "b"],
            "hidden": false
          },
          "Y": { "type": "boolean" }
        }
      }
    }
  ]
}`
	changed, err := ToolCatalogShapeChanged([]byte(catalogShapeBase), []byte(head))
	if err != nil {
		t.Fatalf("shape compare failed: %v", err)
	}
	if !changed {
		t.Fatal("expected mixed description and property add to count as a shape change")
	}
}

func assertSameShapeDigest(t *testing.T, left string, right string) {
	t.Helper()
	leftDigest, err := ToolCatalogShapeDigest([]byte(left))
	if err != nil {
		t.Fatalf("left digest failed: %v", err)
	}
	rightDigest, err := ToolCatalogShapeDigest([]byte(right))
	if err != nil {
		t.Fatalf("right digest failed: %v", err)
	}
	if leftDigest != rightDigest {
		t.Fatalf("expected the same shape digest, got %q and %q", leftDigest, rightDigest)
	}
}

func assertDifferentShapeDigest(t *testing.T, left string, right string) {
	t.Helper()
	leftDigest, err := ToolCatalogShapeDigest([]byte(left))
	if err != nil {
		t.Fatalf("left digest failed: %v", err)
	}
	rightDigest, err := ToolCatalogShapeDigest([]byte(right))
	if err != nil {
		t.Fatalf("right digest failed: %v", err)
	}
	if leftDigest == rightDigest {
		t.Fatalf("expected different shape digests, both were %q", leftDigest)
	}
}
