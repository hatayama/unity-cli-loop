package dispatcher

import (
	"encoding/json"
	"io"

	"github.com/hatayama/unity-cli-loop/common/clicore"
	"github.com/hatayama/unity-cli-loop/dispatcher/dispatchercontract"
)

var (
	dispatcherVersion         = dispatchercontract.DispatcherCurrent.DispatcherVersion
	dispatcherContractVersion = dispatchercontract.DispatcherCurrent.DispatcherContractVersion
)

func writeDispatcherVersionJSON(stdout io.Writer) {
	content, err := json.Marshal(map[string]any{
		"DispatcherVersion":         dispatcherVersion,
		"DispatcherContractVersion": dispatcherContractVersion,
	})
	if err != nil {
		panic(err)
	}
	clicore.WriteLine(stdout, string(content))
}
