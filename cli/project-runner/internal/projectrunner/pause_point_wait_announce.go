package projectrunner

import (
	"fmt"
	"io"
)

const (
	// pausePointEnableAwaitArmedLineFormat is printed to stderr after enable-pause-point
	// --await succeeds and the marker is armed, before the blocking wait. Why: some agent
	// shells cut a foreground call's output window around 30s and drop the wait-end JSON;
	// this line must appear immediately so the marker Id survives that cutoff.
	pausePointEnableAwaitArmedLineFormat = "Pause point armed (Id: %s). Waiting up to %ds for a hit; the JSON response prints only when the wait ends. If this output gets cut off before then, read the outcome with 'uloop pause-point-status --id %s'."

	// pausePointAwaitWaitStartLineFormat is printed to stderr when await-pause-point starts
	// waiting, for the same shell-cutoff reason as the enable --await line.
	pausePointAwaitWaitStartLineFormat = "Waiting for pause point %s (up to %ds). The JSON response prints only when the wait ends. If this output gets cut off before then, read the outcome with 'uloop pause-point-status --id %s'."
)

func announceEnablePausePointAwaitStart(stderr io.Writer, id string, timeoutSeconds int) {
	_, _ = fmt.Fprintln(stderr, fmt.Sprintf(pausePointEnableAwaitArmedLineFormat, id, timeoutSeconds, id))
}

func announceAwaitPausePointWaitStart(stderr io.Writer, id string, timeoutSeconds int) {
	_, _ = fmt.Fprintln(stderr, fmt.Sprintf(pausePointAwaitWaitStartLineFormat, id, timeoutSeconds, id))
}
