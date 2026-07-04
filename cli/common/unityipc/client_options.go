package unityipc

import "time"

// ClientOptions configures optional IPC client timing and progress behavior.
type ClientOptions struct {
	acceptTimeout            time.Duration
	responseTimeout          time.Duration
	heartbeatSilenceOverride time.Duration
	mainThreadStallLimit     time.Duration
	mainThreadStallHandler   func(float64)
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

func (client *Client) nextRequestID() int {
	client.requestIDMu.Lock()
	defer client.requestIDMu.Unlock()
	client.requestID++
	return client.requestID
}
