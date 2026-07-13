package unityprocess

import (
	"context"
	"time"
)

const (
	// ProcessListCommandTimeout bounds ps and PowerShell process-enumeration calls that can hang on WMI stalls.
	ProcessListCommandTimeout = 10 * time.Second
	// FocusCommandTimeout bounds osascript and PowerShell focus calls that can hang on permission dialogs.
	FocusCommandTimeout = 10 * time.Second
)

func withCommandTimeout(ctx context.Context, timeout time.Duration) (context.Context, context.CancelFunc) {
	return context.WithTimeout(ctx, timeout)
}
