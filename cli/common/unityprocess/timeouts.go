package unityprocess

import (
	"context"
	"time"
)

const (
	// ProcessListCommandTimeout bounds the Windows PowerShell process-enumeration call that can hang on WMI stalls.
	// macOS enumeration reads process info via sysctl instead of exec'ing an external command, so it does not use this.
	ProcessListCommandTimeout = 10 * time.Second
	// FocusCommandTimeout bounds osascript and PowerShell focus calls that can hang on permission dialogs.
	FocusCommandTimeout = 10 * time.Second
)

func withCommandTimeout(ctx context.Context, timeout time.Duration) (context.Context, context.CancelFunc) {
	return context.WithTimeout(ctx, timeout)
}
