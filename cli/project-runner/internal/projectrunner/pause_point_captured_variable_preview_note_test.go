package projectrunner

import (
	"encoding/json"
	"strings"
	"testing"
)

const wantCapturedVariablePreviewNote = "a captured value was clipped; re-enable with a larger --max-preview-elements, or read the full value while paused via UloopPausePoint.TryGetCapturedValue in execute-dynamic-code."

// TestApplyPausePointCapturedVariablePreviewNote verifies the CLI note is added only when a
// remaining captured variable (current or history) is truncated, and that the wording is pinned
// to a production-independent literal.
func TestApplyPausePointCapturedVariablePreviewNote(t *testing.T) {
	t.Run("note is set when a current variable is truncated", func(t *testing.T) {
		result := applyPausePointCapturedVariablePreviewNote(pausePointStatusResponse{
			CapturedVariables: []pausePointCapturedVariable{
				{Name: "board", Truncated: true},
			},
		})
		if result.CapturedVariablePreviewNote != wantCapturedVariablePreviewNote {
			t.Fatalf("expected preview-clip note: %q", result.CapturedVariablePreviewNote)
		}
	})

	t.Run("note is omitted when no remaining variable is truncated", func(t *testing.T) {
		result := applyPausePointCapturedVariablePreviewNote(pausePointStatusResponse{
			CapturedVariablesTruncated: true,
			CapturedVariables: []pausePointCapturedVariable{
				{Name: "health"},
			},
		})
		if result.CapturedVariablePreviewNote != "" {
			t.Fatalf("complete listed values must not get the preview-clip note: %q", result.CapturedVariablePreviewNote)
		}
	})

	t.Run("note is set when only a history variable is truncated", func(t *testing.T) {
		result := applyPausePointCapturedVariablePreviewNote(pausePointStatusResponse{
			CapturedVariables: []pausePointCapturedVariable{
				{Name: "health"},
			},
			CapturedVariableHistory: []pausePointCapturedHistoryFrame{
				{
					CapturedVariables: []pausePointCapturedVariable{
						{Name: "board", Truncated: true},
					},
				},
			},
		})
		if result.CapturedVariablePreviewNote != wantCapturedVariablePreviewNote {
			t.Fatalf("expected preview-clip note for a truncated history survivor: %q", result.CapturedVariablePreviewNote)
		}
	})
}

// TestPausePointStatusResponseIncludesCapturedVariablePreviewNote verifies the CLI note
// survives json.Marshal under that exact key.
func TestPausePointStatusResponseIncludesCapturedVariablePreviewNote(t *testing.T) {
	marshaled, err := json.Marshal(pausePointStatusResponse{
		CapturedVariablePreviewNote: pausePointCapturedVariablePreviewNote,
	})
	if err != nil {
		t.Fatalf("marshal failed: %v", err)
	}

	var decoded map[string]json.RawMessage
	if err := json.Unmarshal(marshaled, &decoded); err != nil {
		t.Fatalf("unmarshal envelope failed: %v", err)
	}

	rawNote, ok := decoded["CapturedVariablePreviewNote"]
	if !ok {
		t.Fatalf("CapturedVariablePreviewNote missing from JSON: %s", marshaled)
	}

	var note string
	if err := json.Unmarshal(rawNote, &note); err != nil {
		t.Fatalf("unmarshal note failed: %v", err)
	}
	if note != wantCapturedVariablePreviewNote {
		t.Fatalf("note mismatch: got %#v, want %#v", note, wantCapturedVariablePreviewNote)
	}
}

// TestPausePointStatusResponseOmitsEmptyCapturedVariablePreviewNote verifies an empty
// note is omitted so complete Unity payloads keep their historical shape.
func TestPausePointStatusResponseOmitsEmptyCapturedVariablePreviewNote(t *testing.T) {
	marshaled, err := json.Marshal(pausePointStatusResponse{
		CapturedVariablesTruncated: true,
	})
	if err != nil {
		t.Fatalf("marshal failed: %v", err)
	}

	if strings.Contains(string(marshaled), "CapturedVariablePreviewNote") {
		t.Fatalf("empty CapturedVariablePreviewNote must be omitted from JSON: %s", marshaled)
	}
}
