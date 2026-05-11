package cli

import (
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
