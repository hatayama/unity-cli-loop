package unityipc

import "time"

// ClientOptions configures optional IPC client timing and progress behavior.
type ClientOptions struct {
	acceptTimeout            time.Duration
	responseTimeout          time.Duration
	heartbeatSilenceOverride time.Duration
	mainThreadStallLimit     time.Duration
	mainThreadStallHandler   func(float64)
	selfInducedStallTolerant bool
}

// ClientOption applies one optional IPC client setting.
type ClientOption func(*ClientOptions)

// WithResponseTimeout configures the post-accept response deadline.
func WithResponseTimeout(timeout time.Duration) ClientOption {
	return func(options *ClientOptions) {
		options.responseTimeout = timeout
	}
}

// WithMainThreadStallHandler configures a callback for heartbeat stall reports.
func WithMainThreadStallHandler(handler func(float64)) ClientOption {
	return func(options *ClientOptions) {
		options.mainThreadStallHandler = handler
	}
}

// WithSelfInducedMainThreadStallTolerance exempts a heartbeat's main-thread stall from
// EditorUnresponsiveError when the stall cannot predate this request's own accept (within a
// margin): a command that itself blocks the main thread synchronously (e.g. a long
// execute-dynamic-code snippet) looks identical to a frozen editor on the stall counter alone.
// A stall that already exceeds the elapsed-since-accept time was running before this request
// started and still fails as a genuine freeze.
func WithSelfInducedMainThreadStallTolerance() ClientOption {
	return func(options *ClientOptions) {
		options.selfInducedStallTolerant = true
	}
}

func withAcceptTimeoutForTest(timeout time.Duration) ClientOption {
	return func(options *ClientOptions) {
		options.acceptTimeout = timeout
	}
}

func withHeartbeatSilenceOverrideForTest(timeout time.Duration) ClientOption {
	return func(options *ClientOptions) {
		options.heartbeatSilenceOverride = timeout
	}
}

func withMainThreadStallLimitForTest(timeout time.Duration) ClientOption {
	return func(options *ClientOptions) {
		options.mainThreadStallLimit = timeout
	}
}

func (client *Client) getAcceptTimeout() time.Duration {
	if client.options.acceptTimeout > 0 {
		return client.options.acceptTimeout
	}
	return requestTimeout
}

func (client *Client) getResponseTimeout() time.Duration {
	if client.options.responseTimeout > 0 {
		return client.options.responseTimeout
	}
	return finalResponseTimeout
}

// Derives the sliding silence window for a negotiated heartbeat connection.
// Zero disables sliding: an explicit response timeout (e.g. compile's quick fallback
// to status polling) must stay an absolute deadline, and servers that did not
// negotiate heartbeats keep the legacy absolute deadline too.
func (client *Client) getHeartbeatSilence(heartbeatIntervalSeconds int) time.Duration {
	if client.options.responseTimeout > 0 {
		return 0
	}
	if heartbeatIntervalSeconds <= 0 {
		return 0
	}
	if client.options.heartbeatSilenceOverride > 0 {
		return client.options.heartbeatSilenceOverride
	}
	return time.Duration(heartbeatIntervalSeconds) * time.Second * heartbeatSilenceGraceFactor
}

func (client *Client) getMainThreadStallLimit() time.Duration {
	if client.options.mainThreadStallLimit > 0 {
		return client.options.mainThreadStallLimit
	}
	return defaultMainThreadStallLimit
}

// Why margin: heartbeat delivery and clock skew between accept and the first stall report
// add slack; a stall a few seconds beyond elapsed-since-accept is still self-induced, not
// evidence the freeze predates this request.
const selfInducedStallMargin = 30 * time.Second

func (client *Client) isSelfInducedStall(stallSeconds float64, acceptedAt time.Time) bool {
	if !client.options.selfInducedStallTolerant {
		return false
	}
	elapsedSinceAccept := time.Since(acceptedAt) + selfInducedStallMargin
	return stallSeconds <= elapsedSinceAccept.Seconds()
}

func (client *Client) nextRequestID() int {
	client.requestIDMu.Lock()
	defer client.requestIDMu.Unlock()
	client.requestID++
	return client.requestID
}
