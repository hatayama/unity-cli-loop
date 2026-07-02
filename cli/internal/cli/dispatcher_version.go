package cli

import (
	"encoding/json"
	"io"

	clicontract "github.com/hatayama/unity-cli-loop/cli"
)

var (
	dispatcherVersion         = clicontract.DispatcherCurrent.DispatcherVersion
	dispatcherContractVersion = clicontract.DispatcherCurrent.DispatcherContractVersion
)

func writeDispatcherVersionJSON(stdout io.Writer) {
	content, err := json.Marshal(map[string]any{
		"DispatcherVersion":         dispatcherVersion,
		"DispatcherContractVersion": dispatcherContractVersion,
	})
	if err != nil {
		panic(err)
	}
	writeLine(stdout, string(content))
}
