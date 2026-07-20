package projectrunner

import (
	"context"
	"encoding/json"

	"github.com/hatayama/unity-cli-loop/common/clicontract"
	"github.com/hatayama/unity-cli-loop/common/unityipc"
)

func sendPausePointStatusCommand(
	ctx context.Context,
	connection unityipc.Connection,
	commandName string,
	params map[string]any,
) (pausePointStatusResponse, error) {
	probeContext, cancel := context.WithTimeout(ctx, pausePointStatusProbeTimeout)
	defer cancel()

	result, err := unityipc.NewClient(connection, clicontract.ProjectRunnerVersion()).Send(
		probeContext,
		commandName,
		params,
	)
	if err != nil {
		return pausePointStatusResponse{}, err
	}

	response := pausePointStatusResponse{}
	if err := json.Unmarshal(result, &response); err != nil {
		return pausePointStatusResponse{}, err
	}
	return response, nil
}

func queryPausePointStatusFromUnity(
	ctx context.Context,
	connection unityipc.Connection,
	id string,
) (pausePointStatusResponse, error) {
	return sendPausePointStatusCommand(ctx, connection, pausePointStatusCommandName, map[string]any{"Id": id})
}

func clearPausePointStatusFromUnity(
	ctx context.Context,
	connection unityipc.Connection,
	id string,
) (pausePointStatusResponse, error) {
	return sendPausePointStatusCommand(ctx, connection, pausePointClearStatusCommandName, map[string]any{"Id": id})
}

func extendPausePointExpiryFromUnity(
	ctx context.Context,
	connection unityipc.Connection,
	id string,
	minimumRemainingSeconds int,
) (pausePointStatusResponse, error) {
	return sendPausePointStatusCommand(ctx, connection, pausePointExtendStatusCommandName, map[string]any{
		"Id":                      id,
		"MinimumRemainingSeconds": minimumRemainingSeconds,
	})
}

func clearPausePointAfterWaitTimeout(ctx context.Context, connection unityipc.Connection, id string) {
	clearContext, cancel := context.WithTimeout(ctx, pausePointStatusProbeTimeout)
	defer cancel()
	_, _ = clearPausePointStatus(clearContext, connection, id)
}
