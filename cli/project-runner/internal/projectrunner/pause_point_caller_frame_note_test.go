package projectrunner

import (
	"encoding/json"
	"strings"
	"testing"
)

const wantCallerFrameDynamicMethodNote = "dynamic method (patched by hot reload or pause-point instrumentation); no debug symbols"

// Verifies a set caller-frame Note survives json.Marshal under that exact key.
func TestPausePointCallerFrameIncludesNote(t *testing.T) {
	marshaled, err := json.Marshal(pausePointCallerFrame{
		Method: "Game.Input.HandleJump",
		Note:   wantCallerFrameDynamicMethodNote,
	})
	if err != nil {
		t.Fatalf("marshal failed: %v", err)
	}

	var decoded map[string]json.RawMessage
	if err := json.Unmarshal(marshaled, &decoded); err != nil {
		t.Fatalf("unmarshal envelope failed: %v", err)
	}

	rawNote, ok := decoded["Note"]
	if !ok {
		t.Fatalf("Note missing from JSON: %s", marshaled)
	}

	var note string
	if err := json.Unmarshal(rawNote, &note); err != nil {
		t.Fatalf("unmarshal Note failed: %v", err)
	}
	if note != wantCallerFrameDynamicMethodNote {
		t.Fatalf("Note mismatch: got %#v, want %#v", note, wantCallerFrameDynamicMethodNote)
	}
}

// Verifies an empty Note is omitted so File-bearing frames keep the shared contract shape.
func TestPausePointCallerFrameOmitsEmptyNote(t *testing.T) {
	marshaled, err := json.Marshal(pausePointCallerFrame{
		Method: "Game.AI.Tick",
		File:   "Assets/Scripts/AI.cs",
		Line:   44,
	})
	if err != nil {
		t.Fatalf("marshal failed: %v", err)
	}

	if strings.Contains(string(marshaled), "Note") {
		t.Fatalf("empty Note must be omitted from JSON: %s", marshaled)
	}
}
