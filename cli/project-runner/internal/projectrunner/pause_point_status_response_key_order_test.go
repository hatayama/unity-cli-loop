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
		"HitWhen",
		"HitWhenSkippedCount",
		"HitWhenErrorNote",
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
		"Persisted",
		"SnapshotTiming",
		"StatusNote",
		"HitWhenNote",
		"CapturedVariables",
		"CallerFrames",
		"CapturedVariablesTruncated",
		"TruncatedVariableNames",
		"TruncatedVariableCount",
		"NotCapturableVariables",
		"CapturedVariablesTruncatedNote",
		"CapturedVariablePreviewNote",
		"ClearedReason",
		"StatusBeforeClear",
		"LateHitDiscardedAfterClear",
		"SuppressedByHotReload",
		"RetargetedToHotReloadPatch",
		"LineBasis",
		"SuppressedByHotReloadReason",
		"Warning",
		"Warnings",
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
		HitWhen:                           "speed > 5",
		HitWhenSkippedCount:               2,
		HitWhenErrorNote:                  "--hit-when expected variable 'speed' to be a numeric primitive.",
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
		Persisted:                         true,
		SnapshotTiming:                    "OnEnter",
		StatusNote:                        "read CapturedVariables",
		HitWhenNote:                       "The line executed but no hit matched --hit-when; 2 hit(s) were skipped.",
		CapturedVariables:                 []pausePointCapturedVariable{{Name: "n", Scope: "Local", TypeName: "System.Int32", Value: pausePointVariableValue("1"), UnityObjectKind: "GameObject", UnityObjectPath: "/x", UnityObjectInstanceId: 1, Truncated: true}},
		CallerFrames:                      []pausePointCallerFrame{{Method: "M", File: "Assets/Foo.cs", Line: 10, Note: "n"}},
		CapturedVariablesTruncated:        true,
		TruncatedVariableNames:            []string{"n"},
		TruncatedVariableCount:            1,
		NotCapturableVariables:            []string{"accumulator (ref/out/in parameter cannot be boxed)"},
		CapturedVariablesTruncatedNote:    "note",
		CapturedVariablePreviewNote:       "preview",
		ClearedReason:                     "ExplicitClear",
		StatusBeforeClear:                 "Hit",
		LateHitDiscardedAfterClear:        true,
		SuppressedByHotReload:             true,
		RetargetedToHotReloadPatch:        true,
		LineBasis:                         "LastCompiledSource",
		SuppressedByHotReloadReason:       "reason",
		Warning:                           "w",
		Warnings:                          []string{"w"},
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
			Skipped:      "s",
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

// Verifies the list response carries a populated DomainReloadRearmReport through a
// round-trip with its lines in the order Unity published them, and keeps the key
// last so the re-arm report reads after the pause-point list. Dropping the field
// from pausePointStatusListResponse, or moving it above PausePoints, makes this Red.
func TestPausePointStatusListResponseRoundTripsThePopulatedRearmReport(t *testing.T) {
	report := []string{
		"Re-armed pause point 'Assets/Foo.cs:10' after the domain reload (Assets/Foo.cs:10: `return;`)",
		"Could not re-arm pause point 'Assets/Bar.cs:20' after the domain reload: [PAUSE_POINT_LINE_NOT_RESOLVED] no match",
	}
	raw, err := json.Marshal(pausePointStatusListResponse{
		Success:                 true,
		Message:                 "m",
		Count:                   1,
		PausePoints:             []pausePointStatusListItemResponse{{Id: "Assets/Foo.cs:10", Status: "Armed", Mode: "single-shot", Persisted: true}},
		NextActions:             []string{"await-pause-point"},
		DomainReloadRearmReport: report,
	})
	if err != nil {
		t.Fatalf("marshal pausePointStatusListResponse: %v", err)
	}

	keys, err := collectTopLevelJSONObjectKeys(raw)
	if err != nil {
		t.Fatalf("collect top-level keys: %v\npayload: %s", err, raw)
	}
	expectedKeys := []string{"Success", "Message", "Count", "PausePoints", "NextActions", "DomainReloadRearmReport"}
	if !slices.Equal(keys, expectedKeys) {
		t.Fatalf("top-level JSON key order mismatch\nexpected: %#v\nactual:   %#v", expectedKeys, keys)
	}

	var decoded pausePointStatusListResponse
	if err := json.Unmarshal(raw, &decoded); err != nil {
		t.Fatalf("unmarshal pausePointStatusListResponse: %v\npayload: %s", err, raw)
	}
	if !slices.Equal(decoded.DomainReloadRearmReport, report) {
		t.Fatalf("re-arm report mismatch\nexpected: %#v\nactual:   %#v", report, decoded.DomainReloadRearmReport)
	}
}

// Verifies a list response with no re-arm report omits the key entirely, so a reload
// that replayed nothing does not add an empty section to the output. Dropping
// omitempty from DomainReloadRearmReport makes this Red.
func TestPausePointStatusListResponseOmitsTheRearmReportKeyWhenEmpty(t *testing.T) {
	raw, err := json.Marshal(pausePointStatusListResponse{
		Success:     true,
		Message:     "m",
		Count:       0,
		PausePoints: []pausePointStatusListItemResponse{},
		NextActions: []string{"enable-pause-point"},
	})
	if err != nil {
		t.Fatalf("marshal pausePointStatusListResponse: %v", err)
	}

	keys, err := collectTopLevelJSONObjectKeys(raw)
	if err != nil {
		t.Fatalf("collect top-level keys: %v\npayload: %s", err, raw)
	}
	if slices.Contains(keys, "DomainReloadRearmReport") {
		t.Fatalf("expected DomainReloadRearmReport to be omitted, got keys: %#v", keys)
	}
}
