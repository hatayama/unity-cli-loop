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
}

type controlPlayModeToolResponse struct {
	Success  bool   `json:"Success"`
	IsPaused bool   `json:"IsPaused"`
	Message  string `json:"Message"`
	Warning  string `json:"Warning"`
}

// resumePlayModeForPausePoint is overridden in tests so waitForPausePoint can assert resume/
// trigger ordering without a live Unity connection.
var resumePlayModeForPausePoint = resumePlayModeForPausePointFromUnity

// resumePlayModeForPausePointFromUnity asks control-play-mode for Status, then sends Play only when
// the Editor is currently paused. A Status or Play failure is returned as Error so the wait path
// can skip --trigger without inventing a success.
func resumePlayModeForPausePointFromUnity(
	ctx context.Context,
	connection unityipc.Connection,
) pausePointResumePlayResult {
	resumeContext, cancel := context.WithTimeout(ctx, pausePointResumePlayTimeout)
	defer cancel()

	statusResponse, err := sendControlPlayModeForPausePointResume(resumeContext, connection, "Status")
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

	playResponse, err := sendControlPlayModeForPausePointResume(resumeContext, connection, "Play")
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

func sendControlPlayModeForPausePointResume(
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
