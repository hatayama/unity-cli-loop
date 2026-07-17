package projectrunner

import "encoding/json"

type toolEnvelopeSuccess struct {
	Success *bool `json:"Success"`
}

func toolEnvelopeExitCode(result []byte) int {
	var envelope toolEnvelopeSuccess
	if err := json.Unmarshal(result, &envelope); err != nil || envelope.Success == nil {
		return 1
	}
	if !*envelope.Success {
		return 1
	}
	return 0
}
