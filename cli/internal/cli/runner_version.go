package cli

import (
	"encoding/json"
	"io"

	clicontract "github.com/hatayama/unity-cli-loop/cli"
)

var (
	version         = clicontract.Current.ProjectRunnerVersion
	protocolVersion = clicontract.Current.ProtocolVersion
)

func writeVersionJSON(stdout io.Writer) {
	content, err := json.Marshal(map[string]any{
		"ProjectRunnerVersion": version,
		"ProtocolVersion":      protocolVersion,
	})
	if err != nil {
		panic(err)
	}
	writeLine(stdout, string(content))
}
