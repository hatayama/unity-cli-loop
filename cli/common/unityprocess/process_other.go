//go:build !darwin

package unityprocess

import (
	"context"
	"errors"
)

// listUnityProcessesMac exists so the shared dispatcher in process.go compiles on
// every target OS; the real sysctl-based implementation lives in process_darwin.go
// and this stub is never reached at runtime because listUnityProcesses only calls it
// when runtime.GOOS == "darwin".
func listUnityProcessesMac(_ context.Context) ([]UnityProcess, error) {
	return nil, errors.New("listUnityProcessesMac is unsupported on this platform")
}
