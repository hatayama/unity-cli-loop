package clicore

import (
	"encoding/json"
	"io"

	clicontract "github.com/hatayama/unity-cli-loop/common/clicontract"
)

func WriteVersionJSON(stdout io.Writer) {
	content, err := json.Marshal(map[string]any{
		"ProjectRunnerVersion": clicontract.ProjectRunnerVersion(),
		"ProtocolVersion":      clicontract.ProtocolVersion(),
	})
	if err != nil {
		panic(err)
	}
	WriteLine(stdout, string(content))
}
