package projectrunner

import (
	"encoding/json"
	"strings"
	"testing"

	"github.com/hatayama/unity-cli-loop/common/clicore"
)

// Verifies a flag that belongs to enable-pause-point names its real owner and states that the value
// given at enable time already shows up here, which is the round trip the original message cost:
// the flag exists, so "your runner may be outdated" was never the answer.
func TestParsePausePointStatusUnknownOptionNamesEnableAsTheOwner(t *testing.T) {
	_, err := parsePausePointStatusOptions([]string{"--id", "jump", "--max-preview-elements", "5"})

	if err == nil {
		t.Fatal("expected error for an enable-pause-point flag passed to pause-point-status")
	}
	message := err.Error()
	if !strings.Contains(message, "--max-preview-elements is an enable-pause-point option, not a pause-point-status one.") {
		t.Errorf("owner sentence missing: %s", message)
	}
	if !strings.Contains(
		message,
		"The value passed to enable-pause-point is already applied to the response of this command, "+
			"so it does not need to be passed again here.") {
		t.Errorf("carry-over sentence missing: %s", message)
	}
	if strings.Contains(message, "older than the docs") {
		t.Errorf("stale-runner hint must be gone: %s", message)
	}
}

// Verifies pause-point-status owns --file and --line, so its parser accepts a file:line target.
func TestParsePausePointStatusAcceptsFileLineTarget(t *testing.T) {
	options, err := parsePausePointStatusOptions([]string{
		"--file", "Assets/Scripts/Marker.cs", "--line", "42",
	})
	if err != nil {
		t.Fatalf("file:line target was rejected: %v", err)
	}
	if options.id != "Assets/Scripts/Marker.cs:42" {
		t.Fatalf("id = %q", options.id)
	}
}

// Verifies an enable-only flag that status cannot carry over still names enable as its owner
// without claiming a response value exists for it.
func TestParsePausePointStatusUnknownOptionOmitsCarryOverForNonCarriedFlags(t *testing.T) {
	_, err := parsePausePointStatusOptions([]string{"--id", "jump", "--method", "Player.Jump"})

	if err == nil {
		t.Fatal("expected error for --method passed to pause-point-status")
	}
	message := err.Error()
	if !strings.Contains(message, "--method is an enable-pause-point option, not a pause-point-status one.") {
		t.Errorf("owner sentence missing: %s", message)
	}
	if strings.Contains(message, "already applied to the response") {
		t.Errorf("carry-over sentence must not be claimed for --method: %s", message)
	}
}

// Verifies a flag owned by another runner-owned command names that command, so a flag borrowed from
// await-pause-point is not reported as an enable-pause-point one.
func TestParsePausePointStatusUnknownOptionNamesAwaitAsTheOwner(t *testing.T) {
	_, err := parsePausePointStatusOptions(
		[]string{"--id", "jump", "--" + PausePointLogsMaxCountFlagName, "3"})

	if err == nil {
		t.Fatal("expected error for an await-pause-point flag passed to pause-point-status")
	}
	if !strings.Contains(
		err.Error(),
		"--"+PausePointLogsMaxCountFlagName+" is an await-pause-point option, not a pause-point-status one.") {
		t.Errorf("owner sentence missing: %s", err.Error())
	}
}

// Verifies the article agrees with the command name in both slots of the owner sentence, so a
// vowel-initial command such as await-pause-point does not produce "a await-pause-point one".
func TestPausePointUnknownOptionArticleAgreesWithTheCommandName(t *testing.T) {
	_, err := parseWaitForPausePointOptions(
		[]string{"--id", "jump", "--max-preview-elements", "5"})

	if err == nil {
		t.Fatal("expected error for an enable-pause-point flag passed to await-pause-point")
	}
	if !strings.Contains(
		err.Error(),
		"--max-preview-elements is an enable-pause-point option, not an await-pause-point one.") {
		t.Errorf("article mismatch in the owner sentence: %s", err.Error())
	}

	if article := indefiniteArticleFor(clicore.PausePointStatusUserCommandName); article != "a" {
		t.Errorf("a consonant-initial command takes \"a\", got %q", article)
	}
}

// Verifies the owner reported for a flag several commands accept is fixed rather than dependent on
// map iteration order, so the same misuse always produces the same message.
func TestPausePointUnknownOptionOwnerIsDeterministic(t *testing.T) {
	const flagName = PausePointTimeoutFlagName

	first, ok := pausePointFlagOwnerCommand(flagName)
	if !ok {
		t.Fatalf("--%s must resolve to an owning command", flagName)
	}
	if first != pausePointEnableCommandName {
		t.Errorf("owner of --%s must be the first command in the fixed search order, got %q", flagName, first)
	}
	for attempt := 0; attempt < 20; attempt++ {
		owner, _ := pausePointFlagOwnerCommand(flagName)
		if owner != first {
			t.Fatalf("owner of --%s changed between calls: %q then %q", flagName, first, owner)
		}
	}
}

// Verifies the carry-over sentence is only claimed for the enable-time settings the status response
// actually reports back, so the message never promises evidence the response cannot show.
func TestPausePointCarriedOverEnableFlagsAreVisibleInTheStatusResponse(t *testing.T) {
	response, err := json.Marshal(pausePointStatusResponse{
		Mode:               "continuous",
		MaxHistory:         20,
		HitWhen:            "speed > 5",
		MaxPreviewElements: 5,
		MaxCallerFrames:    4,
		TimeoutSeconds:     30,
	})
	if err != nil {
		t.Fatalf("failed to marshal status response: %v", err)
	}

	carriedOverFields := map[string]string{
		"mode":                 "Mode",
		"max-history":          "MaxHistory",
		"hit-when":             "HitWhen",
		"max-preview-elements": "MaxPreviewElements",
		"max-caller-frames":    "MaxCallerFrames",
		"timeout-seconds":      "TimeoutSeconds",
	}
	if len(carriedOverFields) != len(pausePointCarriedOverEnableFlagNames) {
		t.Fatalf("carry-over flag list changed: %v", pausePointCarriedOverEnableFlagNames)
	}
	for _, flagName := range pausePointCarriedOverEnableFlagNames {
		field, ok := carriedOverFields[flagName]
		if !ok {
			t.Errorf("--%s is described as carried over but has no known status response field", flagName)
			continue
		}
		if !strings.Contains(string(response), `"`+field+`"`) {
			t.Errorf("status response does not report %s for --%s", field, flagName)
		}
		if owner, ok := pausePointFlagOwnerCommand(flagName); !ok || owner != pausePointEnableCommandName {
			t.Errorf("--%s is listed as a carried-over enable flag but resolves to owner %q (found=%v)",
				flagName, owner, ok)
		}
	}
}
