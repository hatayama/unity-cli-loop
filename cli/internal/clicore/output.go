package clicore

import (
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
	var pretty any
	if json.Unmarshal(result, &pretty) != nil {
		WriteLine(stdout, string(result))
		return
	}
	encoder := json.NewEncoder(stdout)
	encoder.SetIndent("", "  ")
	_ = encoder.Encode(pretty)
}
