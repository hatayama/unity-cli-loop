package projectrunner

import (
	"bytes"
	"encoding/json"
)

// encodeCompileResultFields re-encodes a compile result that was taken apart into its top-level
// fields. Why not json.Marshal: it escapes < > & inside the raw values too, and compiler messages
// such as "List<int>" would then reach the reader as < escapes on the rewritten paths only.
// The second value is false when the fields cannot be encoded.
func encodeCompileResultFields(fields map[string]json.RawMessage) (json.RawMessage, bool) {
	var buffer bytes.Buffer
	encoder := json.NewEncoder(&buffer)
	encoder.SetEscapeHTML(false)
	if encoder.Encode(fields) != nil {
		return nil, false
	}
	return json.RawMessage(bytes.TrimRight(buffer.Bytes(), "\n")), true
}
