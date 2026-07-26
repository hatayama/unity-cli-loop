package automation

import (
	"bytes"
	"encoding/json"
	"fmt"
	"sort"
	"strings"
)

// descriptionLocation is where one description string literal sits in the catalog file, quotes
// included, together with the tool and property it belongs to. Property is empty for a tool-level
// description.
type descriptionLocation struct {
	Tool     string
	Property string
	Start    int
	End      int
}

// replaceCatalogDescriptions rewrites only the description string literals the caller asks for and
// returns the whole file otherwise byte-identical.
//
// A decode-edit-encode round trip through tools.ToolDefinition is deliberately not used: the struct
// drops zero-value defaults ("default": 0 / false / ""), adds an empty parameterSchema to every
// tool, and reorders properties from Unity's declaration order into map order. All three would be
// invisible in the generator's own tests and glaring in the catalog, so the edit is textual and the
// bytes around it never move.
func replaceCatalogDescriptions(content []byte, replacements map[descriptionKey]string) ([]byte, error) {
	locations, err := collectDescriptionLocations(content)
	if err != nil {
		return nil, err
	}

	// Applied back to front so an earlier edit never shifts a later offset.
	sort.Slice(locations, func(first int, second int) bool {
		return locations[first].Start > locations[second].Start
	})

	edited := content
	for _, location := range locations {
		description, ok := replacements[descriptionKey{Tool: location.Tool, Property: location.Property}]
		if !ok {
			continue
		}
		encoded, err := encodeJSONString(description)
		if err != nil {
			return nil, err
		}
		edited = append(edited[:location.Start:location.Start], append(encoded, edited[location.End:]...)...)
	}
	return edited, nil
}

// encodeJSONString encodes one string the way the catalog is written: HTML escaping off, so a "<" in
// a description stays a "<" instead of becoming "<" and rewriting bytes nobody asked to change.
func encodeJSONString(value string) ([]byte, error) {
	buffer := bytes.Buffer{}
	encoder := json.NewEncoder(&buffer)
	encoder.SetEscapeHTML(false)
	if err := encoder.Encode(value); err != nil {
		return nil, err
	}
	return []byte(strings.TrimSuffix(buffer.String(), "\n")), nil
}

// collectDescriptionLocations walks the catalog and records the byte range of every tool-level and
// property-level description literal.
func collectDescriptionLocations(content []byte) ([]descriptionLocation, error) {
	decoder := json.NewDecoder(bytes.NewReader(content))
	walker := &catalogWalker{content: content}
	if err := walker.walkValue(decoder, nil); err != nil {
		return nil, err
	}

	locations := make([]descriptionLocation, 0, len(walker.pending))
	for _, location := range walker.pending {
		toolName, ok := walker.toolNames[location.Tool]
		if !ok {
			return nil, fmt.Errorf("tool at index %s has no name field", location.Tool)
		}
		location.Tool = toolName
		locations = append(locations, location)
	}
	return locations, nil
}

type catalogWalker struct {
	content []byte
	// toolNames is filled as "tools" is walked, keyed by array index. A tool's name may appear after
	// its description, so pending ranges are resolved to tool names only once the walk is done.
	toolNames map[string]string
	pending   []descriptionLocation
}

func (walker *catalogWalker) walkValue(decoder *json.Decoder, path []string) error {
	token, err := decoder.Token()
	if err != nil {
		return err
	}

	switch typed := token.(type) {
	case json.Delim:
		switch typed {
		case '{':
			return walker.walkObject(decoder, path)
		case '[':
			return walker.walkArray(decoder, path)
		}
		return fmt.Errorf("unexpected delimiter %q at path %s", typed, strings.Join(path, "."))
	case string:
		walker.recordString(decoder, path, typed)
		return nil
	default:
		return nil
	}
}

func (walker *catalogWalker) walkObject(decoder *json.Decoder, path []string) error {
	for decoder.More() {
		keyToken, err := decoder.Token()
		if err != nil {
			return err
		}
		key, ok := keyToken.(string)
		if !ok {
			return fmt.Errorf("object key was not a string at path %s", strings.Join(path, "."))
		}
		if err := walker.walkValue(decoder, append(path, key)); err != nil {
			return err
		}
	}
	_, err := decoder.Token()
	return err
}

func (walker *catalogWalker) walkArray(decoder *json.Decoder, path []string) error {
	index := 0
	for decoder.More() {
		if err := walker.walkValue(decoder, append(path, fmt.Sprint(index))); err != nil {
			return err
		}
		index++
	}
	_, err := decoder.Token()
	return err
}

func (walker *catalogWalker) recordString(decoder *json.Decoder, path []string, value string) {
	toolIndex, remainder, ok := catalogToolPath(path)
	if !ok {
		return
	}

	if len(remainder) == 1 && remainder[0] == "name" {
		if walker.toolNames == nil {
			walker.toolNames = map[string]string{}
		}
		walker.toolNames[toolIndex] = value
		return
	}

	property, ok := catalogDescriptionProperty(remainder)
	if !ok {
		return
	}
	start, end, ok := stringLiteralRange(walker.content, int(decoder.InputOffset()))
	if !ok {
		return
	}
	walker.pending = append(walker.pending, descriptionLocation{
		Tool:     toolIndex,
		Property: property,
		Start:    start,
		End:      end,
	})
}

// catalogToolPath splits a path such as ["tools","3","inputSchema",...] into the tool index and the
// remainder below it.
func catalogToolPath(path []string) (string, []string, bool) {
	if len(path) < 2 || path[0] != "tools" {
		return "", nil, false
	}
	return path[1], path[2:], true
}

// catalogDescriptionProperty reports which description a path below a tool points at: the tool's own
// ("") or one property's (the property name).
func catalogDescriptionProperty(remainder []string) (string, bool) {
	if len(remainder) == 1 && remainder[0] == "description" {
		return "", true
	}
	if len(remainder) != 4 || remainder[1] != "properties" || remainder[3] != "description" {
		return "", false
	}
	if remainder[0] != "inputSchema" && remainder[0] != "parameterSchema" {
		return "", false
	}
	return remainder[2], true
}

// stringLiteralRange finds the string literal, quotes included, that ends at endOffset. Scanning
// backwards for the opening quote keeps the range exact even for a value carrying escapes, which
// re-encoding the decoded value could not guarantee.
func stringLiteralRange(content []byte, endOffset int) (int, int, bool) {
	if endOffset <= 0 || endOffset > len(content) || content[endOffset-1] != '"' {
		return 0, 0, false
	}
	for index := endOffset - 2; index >= 0; index-- {
		if content[index] != '"' {
			continue
		}
		backslashes := 0
		for probe := index - 1; probe >= 0 && content[probe] == '\\'; probe-- {
			backslashes++
		}
		if backslashes%2 == 0 {
			return index, endOffset, true
		}
	}
	return 0, 0, false
}
