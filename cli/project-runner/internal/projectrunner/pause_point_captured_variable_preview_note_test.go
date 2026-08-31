package projectrunner

import (
	"encoding/json"
	"strings"
	"testing"
)

const wantCapturedVariablePreviewNoteAtSevenElements = "a captured value was clipped at the current --max-preview-elements cap of 7 elements; re-enable with a larger cap to widen future previews, but first read any CapturedVariables and CapturedVariableHistory you still need with pause-point-status, because re-enabling starts a new generation and discards them. While Unity is still paused, UloopPausePoint.TryGetCapturedValue in execute-dynamic-code returns the full live value."

const wantCapturedVariablePreviewNoteAtTwentyThreeElements = "a captured value was clipped at the current --max-preview-elements cap of 23 elements; re-enable with a larger cap to widen future previews, but first read any CapturedVariables and CapturedVariableHistory you still need with pause-point-status, because re-enabling starts a new generation and discards them. While Unity is still paused, UloopPausePoint.TryGetCapturedValue in execute-dynamic-code returns the full live value."

const wantCapturedVariablePreviewNoteAtFortyTwoElements = "a captured value was clipped at the current --max-preview-elements cap of 42 elements; re-enable with a larger cap to widen future previews, but first read any CapturedVariables and CapturedVariableHistory you still need with pause-point-status, because re-enabling starts a new generation and discards them. While Unity is still paused, UloopPausePoint.TryGetCapturedValue in execute-dynamic-code returns the full live value."

// TestApplyPausePointCapturedVariablePreviewNote verifies the CLI note is added only when a
// remaining captured variable (current or history) is truncated, and that the wording is pinned
// to a production-independent literal.
func TestApplyPausePointCapturedVariablePreviewNote(t *testing.T) {
	t.Run("note is set when a current variable is truncated", func(t *testing.T) {
		result := applyPausePointCapturedVariablePreviewNote(pausePointStatusResponse{
			MaxPreviewElements: 7,
			CapturedVariables: []pausePointCapturedVariable{
				{Name: "board", Truncated: true},
			},
		})
		if result.CapturedVariablePreviewNote != wantCapturedVariablePreviewNoteAtSevenElements {
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
			MaxPreviewElements: 23,
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
		if result.CapturedVariablePreviewNote != wantCapturedVariablePreviewNoteAtTwentyThreeElements {
			t.Fatalf("expected preview-clip note for a truncated history survivor: %q", result.CapturedVariablePreviewNote)
		}
	})
}

// TestPausePointStatusResponseIncludesCapturedVariablePreviewNote verifies the CLI note
// survives json.Marshal under that exact key.
func TestPausePointStatusResponseIncludesCapturedVariablePreviewNote(t *testing.T) {
	marshaled, err := json.Marshal(pausePointStatusResponse{
		CapturedVariablePreviewNote: wantCapturedVariablePreviewNoteAtFortyTwoElements,
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
	if note != wantCapturedVariablePreviewNoteAtFortyTwoElements {
		t.Fatalf("note mismatch: got %#v, want %#v", note, wantCapturedVariablePreviewNoteAtFortyTwoElements)
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
