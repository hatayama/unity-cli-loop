package unityprocess

import (
	"runtime"
	"testing"
)

// Verifies the held-mouse-button reading is available on Windows, where it comes from
// GetAsyncKeyState, and reports itself unavailable everywhere else instead of guessing.
func TestHeldMouseButtonsReportsAvailabilityByPlatform(t *testing.T) {
	buttons, available := HeldMouseButtons()

	if runtime.GOOS != "windows" {
		if available || buttons != nil {
			t.Fatalf("HeldMouseButtons must be unavailable on %s, got %v %v", runtime.GOOS, buttons, available)
		}
		return
	}
	if !available || buttons == nil {
		t.Fatalf("HeldMouseButtons must answer with a list on Windows, got %v %v", buttons, available)
	}
}

// Verifies the GetAsyncKeyState high bit is what marks a button as held, so a stale "pressed since
// last call" low bit never reports a button that is already up.
func TestMouseButtonHeldReadsTheHighBit(t *testing.T) {
	if !mouseButtonHeld(0x8000) {
		t.Fatal("high bit set must read as held")
	}
	if mouseButtonHeld(0x0001) {
		t.Fatal("low bit alone must not read as held")
	}
	if mouseButtonHeld(0) {
		t.Fatal("zero must not read as held")
	}
}
