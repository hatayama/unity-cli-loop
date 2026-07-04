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
