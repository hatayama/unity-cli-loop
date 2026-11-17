package dispatcher

import (
	"context"
	"encoding/json"
	"errors"

	"github.com/hatayama/unity-cli-loop/common/clicontract"
	clierrors "github.com/hatayama/unity-cli-loop/common/errors"
	"github.com/hatayama/unity-cli-loop/common/unityipc"
)

const (
	cliUpdateRequiredRPCDataType   = "cli_update_required"
	launchOlderPackageProtocolNote = "This uloop dispatcher speaks a newer IPC protocol than this project's Unity package, " +
		"so launch confirmed only that the Editor answers; other uloop commands use the project's pinned runner and are unaffected."
)

// cliUpdateRequiredData is the part of the package's protocol-mismatch error launch reads. The
// pointer tells a missing or null requiredProtocolVersion apart from a real value.
type cliUpdateRequiredData struct {
	RequiredProtocolVersion *int `json:"requiredProtocolVersion"`
}

// isNewerDispatcherProtocolMismatch reports whether err is the package refusing this dispatcher
// only because the dispatcher speaks a newer IPC protocol than the package. The refusal itself
// proves the Editor's IPC server is up and answering, which is all launch can confirm then.
func isNewerDispatcherProtocolMismatch(err error) bool {
	var rpcErr *unityipc.RPCError
	if !errors.As(err, &rpcErr) {
		return false
	}
	if clierrors.RPCDataType(rpcErr.Data) != cliUpdateRequiredRPCDataType {
		return false
	}
	data := cliUpdateRequiredData{}
	if err := json.Unmarshal(rpcErr.Data, &data); err != nil || data.RequiredProtocolVersion == nil {
		return false
	}
	requiredProtocolVersion := *data.RequiredProtocolVersion
	return requiredProtocolVersion >= 1 && requiredProtocolVersion < clicontract.ProtocolVersion()
}

// probeProjectIpcAnswers reports whether the project's IPC answers when the process scan failed,
// counting a newer-dispatcher protocol refusal as an answer.
func probeProjectIpcAnswers(ctx context.Context, projectRoot string, deps launchDeps) (bool, launchReadiness) {
	err := deps.probeProjectIpcFallback(ctx, projectRoot)
	if err == nil {
		return true, launchReadiness{}
	}
	if isNewerDispatcherProtocolMismatch(err) {
		return true, launchReadiness{olderPackageProtocol: true}
	}
	return false, launchReadiness{}
}
