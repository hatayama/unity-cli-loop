package clicore

import (
	"encoding/json"
	"io"

	clicontract "github.com/hatayama/unity-cli-loop/common/clicontract"
)

// Version returns the project runner release version advertised by CLI commands.
func Version() string {
	return clicontract.ProjectRunnerVersion()
}

// ProtocolVersion returns the IPC protocol generation advertised by CLI commands.
func ProtocolVersion() int {
	return clicontract.ProtocolVersion()
}

func WriteVersionJSON(stdout io.Writer) {
	content, err := json.Marshal(map[string]any{
		"ProjectRunnerVersion": Version(),
		"ProtocolVersion":      ProtocolVersion(),
	})
	if err != nil {
		panic(err)
	}
	WriteLine(stdout, string(content))
}
