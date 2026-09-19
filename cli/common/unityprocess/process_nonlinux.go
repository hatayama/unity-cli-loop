//go:build !linux

package unityprocess

import (
	"context"
	"errors"
)

// listUnityProcessesLinux exists so the shared dispatcher in process.go compiles on
// every target OS; the real /proc-based implementation lives in process_linux.go
// and this stub is never reached at runtime because listUnityProcesses only calls it
// when runtime.GOOS == "linux".
func listUnityProcessesLinux(_ context.Context) ([]UnityProcess, error) {
	return nil, errors.New("listUnityProcessesLinux is unsupported on this platform")
}
