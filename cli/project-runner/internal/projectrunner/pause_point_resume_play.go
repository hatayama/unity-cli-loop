package projectrunner

import (
	"context"
	"encoding/json"
	"fmt"
	"time"

	"github.com/hatayama/unity-cli-loop/common/clicontract"
	"github.com/hatayama/unity-cli-loop/common/unityipc"
)

const (
	pausePointResumePlayCommandName = "control-play-mode"
	pausePointResumePlayTimeout     = 10 * time.Second
)

// pausePointResumePlayResult reports what --resume-play did (or why it skipped). Set only when
// --resume-play was passed, mirroring TriggerResult's omit-when-unused contract.
type pausePointResumePlayResult struct {
	WasPaused bool   `json:"WasPaused"`
	Resumed   bool   `json:"Resumed"`
	Error     string `json:"Error,omitempty"`

	// Repaused reports that the wait put PlayMode back into pause after resuming it, which happens
	// when the wait is abandoned because the --trigger command was rejected before it ran. Reported
	// rather than kept internal: a caller that asked for a resume must be able to see that the
	// Editor is paused again, otherwise the next command's behavior looks unexplained.
	Repaused bool `json:"Repaused,omitempty"`

	// RepauseError explains why the re-pause failed. Not hidden: a failed re-pause means gameplay
	// keeps running and can still consume the preserved marker's single shot.
	RepauseError string `json:"RepauseError,omitempty"`
}

type controlPlayModeToolResponse struct {
	Success  bool   `json:"Success"`
	IsPaused bool   `json:"IsPaused"`
	Message  string `json:"Message"`
}

// resumePlayModeForPausePoint is overridden in tests so waitForPausePoint can assert resume/
// trigger ordering without a live Unity connection.
var resumePlayModeForPausePoint = resumePlayModeForPausePointFromUnity

// sendControlPlayModeForPausePoint is overridden in tests so resumePlayModeForPausePointFromUnity's
// Status/Play branches and the Pause request sent when a wait is abandoned can be exercised
// without IPC.
var sendControlPlayModeForPausePoint = sendControlPlayModeForPausePointFromUnity

// resumePlayModeForPausePointFromUnity asks control-play-mode for Status, then sends Play only when
// the Editor is currently paused. A Status or Play failure is returned as Error so the wait path
// can skip --trigger without inventing a success.
func resumePlayModeForPausePointFromUnity(
	ctx context.Context,
	connection unityipc.Connection,
) pausePointResumePlayResult {
	resumeContext, cancel := context.WithTimeout(ctx, pausePointResumePlayTimeout)
	defer cancel()

	statusResponse, err := sendControlPlayModeForPausePoint(resumeContext, connection, "Status")
	if err != nil {
		return pausePointResumePlayResult{
			Error: fmt.Sprintf("control-play-mode Status failed: %v", err),
		}
	}
	if !statusResponse.Success {
		message := statusResponse.Message
		if message == "" {
			message = "control-play-mode Status returned Success=false"
		}
		return pausePointResumePlayResult{Error: message}
	}

	if !statusResponse.IsPaused {
		return pausePointResumePlayResult{WasPaused: false, Resumed: false}
	}

	playResponse, err := sendControlPlayModeForPausePoint(resumeContext, connection, "Play")
	if err != nil {
		return pausePointResumePlayResult{
			WasPaused: true,
			Resumed:   false,
			Error:     fmt.Sprintf("control-play-mode Play failed: %v", err),
		}
	}
	if !playResponse.Success {
		message := playResponse.Message
		if message == "" {
			message = "control-play-mode Play returned Success=false"
		}
		return pausePointResumePlayResult{
			WasPaused: true,
			Resumed:   false,
			Error:     message,
		}
	}

	return pausePointResumePlayResult{WasPaused: true, Resumed: true}
}

// repausePlayModeAfterAbandonedWait puts PlayMode back into pause after this command's own
// --resume-play resumed it, once the wait is abandoned because the --trigger command was rejected
// before it ran. Why re-pause: the marker is deliberately left armed so the trigger can be fixed
// and retried, but a marker left armed while gameplay keeps running can have its single shot
// consumed by unrelated game activity reaching the same line — the retry would then return
// someone else's hit as if it were the fixed trigger's result.
func repausePlayModeAfterAbandonedWait(
	ctx context.Context,
	connection unityipc.Connection,
	resumeResult pausePointResumePlayResult,
) pausePointResumePlayResult {
	pauseContext, cancel := context.WithTimeout(ctx, pausePointResumePlayTimeout)
	defer cancel()

	response, err := sendControlPlayModeForPausePoint(pauseContext, connection, "Pause")
	if err != nil {
		resumeResult.RepauseError = fmt.Sprintf("control-play-mode Pause failed: %v", err)
		return resumeResult
	}
	if !response.Success {
		message := response.Message
		if message == "" {
			message = "control-play-mode Pause returned Success=false"
		}
		resumeResult.RepauseError = message
		return resumeResult
	}

	resumeResult.Repaused = true
	return resumeResult
}

func sendControlPlayModeForPausePointFromUnity(
	ctx context.Context,
	connection unityipc.Connection,
	action string,
) (controlPlayModeToolResponse, error) {
	result, err := unityipc.NewClient(connection, clicontract.ProjectRunnerVersion()).Send(
		ctx,
		pausePointResumePlayCommandName,
		map[string]any{"Action": action},
	)
	if err != nil {
		return controlPlayModeToolResponse{}, err
	}

	response := controlPlayModeToolResponse{}
	if err := json.Unmarshal(result, &response); err != nil {
		return controlPlayModeToolResponse{}, err
	}
	return response, nil
}
