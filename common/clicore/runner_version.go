package clicore

import (
	"encoding/json"
	"io"

	clicontract "github.com/hatayama/unity-cli-loop/common/clicontract"
)

var (
	Version         = clicontract.Current.ProjectRunnerVersion
	ProtocolVersion = clicontract.Current.ProtocolVersion
)

func WriteVersionJSON(stdout io.Writer) {
	content, err := json.Marshal(map[string]any{
		"ProjectRunnerVersion": Version,
		"ProtocolVersion":      ProtocolVersion,
	})
	if err != nil {
		panic(err)
	}
	WriteLine(stdout, string(content))
}
