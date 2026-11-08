//go:build windows

package unityprocess

import "testing"

// Verifies every button name HeldMouseButtons can report comes from the fixed button list, so the
// diagnostics never show a name the documentation does not list.
func TestHeldMouseButtonsReportsOnlyKnownNames(t *testing.T) {
	known := map[string]bool{}
	for _, button := range heldMouseButtons {
		known[button.name] = true
	}

	buttons, _ := HeldMouseButtons()
	for _, button := range buttons {
		if !known[button] {
			t.Fatalf("unknown button name %q in %v", button, buttons)
		}
	}
}
