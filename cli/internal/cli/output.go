package cli

import (
	"encoding/json"
	"fmt"
	"io"
)

func writeLine(writer io.Writer, values ...any) {
	// CLI status output failures are not recoverable after command outcome is decided.
	_, _ = fmt.Fprintln(writer, values...)
}

func writeFormat(writer io.Writer, format string, values ...any) {
	// CLI status output failures are not recoverable after command outcome is decided.
	_, _ = fmt.Fprintf(writer, format, values...)
}

func writeJSON(stdout io.Writer, result json.RawMessage) {
	var pretty any
	if json.Unmarshal(result, &pretty) != nil {
		writeLine(stdout, string(result))
		return
	}
	encoder := json.NewEncoder(stdout)
	encoder.SetIndent("", "  ")
	_ = encoder.Encode(pretty)
}
