package projectrunner

import (
	"context"
	"reflect"
	"testing"
)

// Verifies the matching-log query asks Unity for the marker text and fills counts Unity left at
// zero from the logs it actually returned.
func TestFetchMatchingLogsFromUnityFillsMissingCounts(t *testing.T) {
	server := startFakeUnityResultServer(t, t.TempDir(), pausePointGetLogsCommandName,
		`{"Success":true,"Logs":[{"Type":"Log","Message":"hit a"},{"Type":"Log","Message":"hit b"}]}`)

	result, err := fetchMatchingLogsFromUnity(context.Background(), server.connection, "marker-1", 20)
	if err != nil {
		t.Fatalf("fetch failed: %v", err)
	}
	request := server.receivedRequest(t)
	if request["SearchText"] != "marker-1" || request["MaxCount"] != float64(20) {
		t.Fatalf("unexpected request: %#v", request)
	}
	want := pausePointMatchingLogsResult{
		SearchText:     "marker-1",
		TotalCount:     2,
		DisplayedCount: 2,
		MaxCount:       20,
		Logs: []pausePointMatchingLog{
			{Type: "Log", Message: "hit a"},
			{Type: "Log", Message: "hit b"},
		},
	}
	if !reflect.DeepEqual(result, want) {
		t.Fatalf("result mismatch:\nwant: %#v\ngot:  %#v", want, result)
	}
}

// Verifies counts and search text Unity did report are kept, and a missing log list becomes empty.
func TestFetchMatchingLogsFromUnityKeepsReportedValues(t *testing.T) {
	server := startFakeUnityResultServer(t, t.TempDir(), pausePointGetLogsCommandName,
		`{"Success":true,"SearchText":"echoed","TotalCount":9,"DisplayedCount":0,"MaxCount":5,"LogType":"All","IncludeStackTrace":true}`)

	result, err := fetchMatchingLogsFromUnity(context.Background(), server.connection, "marker-2", 20)
	if err != nil {
		t.Fatalf("fetch failed: %v", err)
	}
	server.receivedRequest(t)
	want := pausePointMatchingLogsResult{
		SearchText:        "echoed",
		TotalCount:        9,
		MaxCount:          5,
		LogType:           "All",
		IncludeStackTrace: true,
		Logs:              []pausePointMatchingLog{},
	}
	if !reflect.DeepEqual(result, want) {
		t.Fatalf("result mismatch:\nwant: %#v\ngot:  %#v", want, result)
	}
}

// Verifies Unity errors and undecodable results are returned as errors.
func TestFetchMatchingLogsFromUnityReportsFailures(t *testing.T) {
	t.Run("Unity error", func(t *testing.T) {
		server := startFakeUnityServer(t, t.TempDir(), pausePointGetLogsCommandName, testUnityRPCFailureResponse)
		if _, err := fetchMatchingLogsFromUnity(context.Background(), server.connection, "m", 1); err == nil {
			t.Fatal("expected the Unity error")
		}
	})
	t.Run("undecodable result", func(t *testing.T) {
		server := startFakeUnityResultServer(t, t.TempDir(), pausePointGetLogsCommandName, `[1]`)
		if _, err := fetchMatchingLogsFromUnity(context.Background(), server.connection, "m", 1); err == nil {
			t.Fatal("expected a decode error")
		}
	})
}

// Verifies each independent doubt about single-fire evidence becomes its own warning entry.
func TestBuildPausePointLogWarningsListsEachDoubt(t *testing.T) {
	logs := pausePointMatchingLogsResult{TotalCount: 1, Logs: []pausePointMatchingLog{{Message: "a"}, {Message: "b"}}}

	warnings := buildPausePointLogWarnings(logs, 2)

	want := []string{
		"Multiple matching logs were observed for this pause point id; inspect MatchingLogs before treating the scenario as single-fire evidence.",
		"The pause point reports multiple hits; inspect the paused state before treating the scenario as single-fire evidence.",
	}
	if !reflect.DeepEqual(warnings, want) {
		t.Fatalf("warnings mismatch:\nwant: %#v\ngot:  %#v", want, warnings)
	}
}
