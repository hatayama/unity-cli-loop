package progress

// Stage identifies which phase produced a progress event.
type Stage string

const (
	// StageConnected means the client reached the Unity IPC endpoint.
	StageConnected Stage = "connected"

	// StageAccepted means Unity accepted the request and may stream heartbeats.
	StageAccepted Stage = "accepted"

	// StageMessage carries display-ready progress text.
	StageMessage Stage = "message"
)

// Event reports structured progress without tying UI packages to IPC details.
type Event struct {
	Stage   Stage
	Message string
}

// Func receives structured progress events.
type Func func(Event)
