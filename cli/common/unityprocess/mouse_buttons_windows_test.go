//go:build windows

package unityprocess

import "testing"

// Verifies the button table HeldMouseButtons reports from carries exactly the names the compile
// skill documents for HeldMouseButtons, so a renamed or added entry is caught against that contract
// rather than against the table itself.
func TestHeldMouseButtonTableMatchesDocumentedNames(t *testing.T) {
	documented := []string{"left", "right", "middle", "x1", "x2"}
	if len(heldMouseButtons) != len(documented) {
		t.Fatalf("button table has %d entries, want %d: %v", len(heldMouseButtons), len(documented), documented)
	}
	for index, button := range heldMouseButtons {
		if button.name != documented[index] {
			t.Fatalf("button %d is named %q, want %q", index, button.name, documented[index])
		}
	}
}

// Verifies every button name HeldMouseButtons reports is one of the documented names.
func TestHeldMouseButtonsReportsOnlyDocumentedNames(t *testing.T) {
	documented := map[string]bool{"left": true, "right": true, "middle": true, "x1": true, "x2": true}

	buttons, _ := HeldMouseButtons()
	for _, button := range buttons {
		if !documented[button] {
			t.Fatalf("undocumented button name %q in %v", button, buttons)
		}
	}
}
