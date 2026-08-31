package clicore

import (
	"bytes"
	"encoding/json"
	"fmt"
	"io"
)

func WriteLine(writer io.Writer, values ...any) {
	// CLI status output failures are not recoverable after command outcome is decided.
	_, _ = fmt.Fprintln(writer, values...)
}

func WriteFormat(writer io.Writer, format string, values ...any) {
	// CLI status output failures are not recoverable after command outcome is decided.
	_, _ = fmt.Fprintf(writer, format, values...)
}

func WriteJSON(stdout io.Writer, result json.RawMessage) {
	// Why json.Indent instead of unmarshal/re-encode: decoding into any turns
	// integers above float64 precision into corrupted values, while Indent
	// reformats the raw bytes without touching them.
	var indented bytes.Buffer
	if json.Indent(&indented, result, "", "  ") != nil {
		WriteLine(stdout, string(result))
		return
	}
	WriteLine(stdout, indented.String())
}
