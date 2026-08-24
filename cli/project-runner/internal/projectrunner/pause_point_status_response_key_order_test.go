package projectrunner

import (
	"bytes"
	"encoding/json"
	"fmt"
	"slices"
	"testing"
)

// Verifies pause-point stdout key order places SnapshotTiming and StatusNote
// immediately before CapturedVariables so agents read those notes before the
// variable dump. Mutating the struct declarations back to the old positions
// (CapturedVariables first, notes after ResolvedMethod / history notes) makes
// this test Red.
func TestPausePointStatusResponseMarshalsReadingNotesBeforeCapturedVariables(t *testing.T) {
	raw, err := json.Marshal(fullyPopulatedPausePointStatusResponse())
	if err != nil {
		t.Fatalf("marshal pausePointStatusResponse: %v", err)
	}

	actual, err := collectTopLevelJSONObjectKeys(raw)
	if err != nil {
		t.Fatalf("collect top-level keys: %v\npayload: %s", err, raw)
	}

	expected := []string{
		"Success",
		"ErrorCode",
		"Id",
		"Status",
		"IsEnabled",
		"IsHit",
		"HitCount",
		"MethodEntryCount",
		"TimeoutSeconds",
		"Mode",
		"MaxHistory",
		"MaxPreviewElements",
		"MaxCallerFrames",
		"CapturedVariableHistory",
		"HistoryDroppedCount",
		"Expired",
		"EnabledAtUtc",
		"ElapsedSinceEnabledMilliseconds",
		"RemainingMilliseconds",
		"Generation",
		"EditorState",
		"FirstHitAtUtc",
		"LastHitAtUtc",
		"FirstHitSequence",
		"LastHitSequence",
		"Message",
		"RecommendedNextAction",
		"SnapshotTiming",
		"StatusNote",
		"CapturedVariables",
		"CallerFrames",
		"CapturedVariablesTruncated",
		"TruncatedVariableNames",
		"TruncatedVariableCount",
		"CapturedVariablesTruncatedNote",
		"CapturedVariablePreviewNote",
		"ClearedReason",
		"StatusBeforeClear",
		"LateHitDiscardedAfterClear",
		"SuppressedByHotReload",
		"RetargetedToHotReloadPatch",
		"SuppressedByHotReloadReason",
		"Warning",
		"ResolvedLine",
		"ResolvedLineText",
		"ResolvedMethod",
		"CapturedVariableNameFilterNoMatch",
		"CapturedVariableNamesNotFound",
		"CapturedVariableHistoryNote",
		"TriggerResult",
		"ResumePlayResult",
		"TriggerFailed",
	}
	if !slices.Equal(actual, expected) {
		t.Fatalf("top-level JSON key order mismatch\nexpected: %#v\nactual:   %#v", expected, actual)
	}
}

func fullyPopulatedPausePointStatusResponse() pausePointStatusResponse {
	triggerFailed := true
	return pausePointStatusResponse{
		Success:                           true,
		ErrorCode:                         "NONE",
		Id:                                "Assets/Foo.cs:10",
		Status:                            "Hit",
		IsEnabled:                         true,
		IsHit:                             true,
		HitCount:                          1,
		MethodEntryCount:                  1,
		TimeoutSeconds:                    30,
		Mode:                              "single-shot",
		MaxHistory:                        8,
		MaxPreviewElements:                16,
		MaxCallerFrames:                   4,
		CapturedVariableHistory:           []pausePointCapturedHistoryFrame{{HitSequence: 1, FrameCount: 2, HitAtUtc: "t", CapturedVariables: []pausePointCapturedVariable{{Name: "n", Scope: "Local", TypeName: "System.Int32", Value: pausePointVariableValue("1")}}, Truncated: true, CallerFrames: []pausePointCallerFrame{{Method: "M"}}}},
		HistoryDroppedCount:               1,
		Expired:                           true,
		EnabledAtUtc:                      "t",
		ElapsedSinceEnabledMilliseconds:   1,
		RemainingMilliseconds:             1,
		Generation:                        1,
		EditorState:                       pausePointEditorState{IsPlaying: true, IsPaused: true, CapturedAt: "t"},
		FirstHitAtUtc:                     "t",
		LastHitAtUtc:                      "t",
		FirstHitSequence:                  1,
		LastHitSequence:                   1,
		Message:                           "m",
		RecommendedNextAction:             "a",
		SnapshotTiming:                    "OnEnter",
		StatusNote:                        "read CapturedVariables",
		CapturedVariables:                 []pausePointCapturedVariable{{Name: "n", Scope: "Local", TypeName: "System.Int32", Value: pausePointVariableValue("1"), UnityObjectKind: "GameObject", UnityObjectPath: "/x", UnityObjectInstanceId: 1, Truncated: true}},
		CallerFrames:                      []pausePointCallerFrame{{Method: "M", File: "Assets/Foo.cs", Line: 10, Note: "n"}},
		CapturedVariablesTruncated:        true,
		TruncatedVariableNames:            []string{"n"},
		TruncatedVariableCount:            1,
		CapturedVariablesTruncatedNote:    "note",
		CapturedVariablePreviewNote:       "preview",
		ClearedReason:                     "ExplicitClear",
		StatusBeforeClear:                 "Hit",
		LateHitDiscardedAfterClear:        true,
		SuppressedByHotReload:             true,
		RetargetedToHotReloadPatch:        true,
		SuppressedByHotReloadReason:       "reason",
		Warning:                           "w",
		ResolvedLine:                      10,
		ResolvedLineText:                  "return;",
		ResolvedMethod:                    "Foo.Bar",
		CapturedVariableNameFilterNoMatch: true,
		CapturedVariableNamesNotFound:     []string{"missing"},
		CapturedVariableHistoryNote:       "history",
		TriggerResult: &pausePointTriggerResult{
			Command:     "simulate-keyboard",
			Completed:   true,
			Response:    json.RawMessage(`{"Success":true}`),
			Error:       "e",
			Explanation: "x",
		},
		ResumePlayResult: &pausePointResumePlayResult{
			WasPaused:    true,
			Resumed:      true,
			Error:        "e",
			Repaused:     true,
			RepauseError: "e",
		},
		TriggerFailed: &triggerFailed,
	}
}

func collectTopLevelJSONObjectKeys(raw []byte) ([]string, error) {
	decoder := json.NewDecoder(bytes.NewReader(raw))
	token, err := decoder.Token()
	if err != nil {
		return nil, err
	}
	delim, ok := token.(json.Delim)
	if !ok || delim != '{' {
		return nil, fmt.Errorf("expected JSON object, got %v", token)
	}

	keys := make([]string, 0)
	for decoder.More() {
		keyToken, err := decoder.Token()
		if err != nil {
			return nil, err
		}
		key, ok := keyToken.(string)
		if !ok {
			return nil, fmt.Errorf("expected object key, got %v", keyToken)
		}
		keys = append(keys, key)
		if err := skipJSONValue(decoder); err != nil {
			return nil, err
		}
	}
	if _, err := decoder.Token(); err != nil {
		return nil, err
	}
	return keys, nil
}

func skipJSONValue(decoder *json.Decoder) error {
	token, err := decoder.Token()
	if err != nil {
		return err
	}
	delim, ok := token.(json.Delim)
	if !ok {
		return nil
	}
	for decoder.More() {
		if delim == '{' {
			if _, err := decoder.Token(); err != nil {
				return err
			}
		}
		if err := skipJSONValue(decoder); err != nil {
			return err
		}
	}
	_, err = decoder.Token()
	return err
}
