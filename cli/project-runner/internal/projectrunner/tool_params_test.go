package projectrunner

import (
	"errors"
	"strings"
	"testing"

	clierrors "github.com/hatayama/unity-cli-loop/common/errors"

	"github.com/hatayama/unity-cli-loop/common/clicore"
)

// Verifies that a string property with an enum accepts an exact-case valid value.
func TestConvertValueAcceptsExactCaseEnumValue(t *testing.T) {
	property := clicore.ToolProperty{Type: "string", Enum: []string{"Play", "Stop", "Pause"}}

	converted, err := convertValue("Stop", property, "--action")
	if err != nil {
		t.Fatalf("expected no error, got: %v", err)
	}
	if converted != "Stop" {
		t.Fatalf("expected converted value %q, got %q", "Stop", converted)
	}
}

// Verifies that enum matching ignores case, mirroring the C# CaseInsensitiveStringEnumConverter.
func TestConvertValueAcceptsCaseInsensitiveEnumValue(t *testing.T) {
	property := clicore.ToolProperty{Type: "string", Enum: []string{"Play", "Stop", "Pause"}}

	converted, err := convertValue("stop", property, "--action")
	if err != nil {
		t.Fatalf("expected no error, got: %v", err)
	}
	if converted != "stop" {
		t.Fatalf("expected the original value to be passed through unchanged, got %q", converted)
	}
}

// Verifies that a value outside the enum is rejected with the valid value list included.
func TestConvertValueRejectsInvalidEnumValue(t *testing.T) {
	property := clicore.ToolProperty{Type: "string", Enum: []string{"Play", "Stop", "Pause"}}

	_, err := convertValue("bogus", property, "--action")

	if err == nil {
		t.Fatal("expected an error for a value outside the enum")
	}
	var argumentError *clierrors.ArgumentError
	if !errors.As(err, &argumentError) {
		t.Fatalf("expected an *ArgumentError, got %T: %v", err, err)
	}
	if argumentError.Received != "bogus" {
		t.Fatalf("expected Received to be %q, got %q", "bogus", argumentError.Received)
	}
	for _, want := range property.Enum {
		if !strings.Contains(argumentError.ExpectedType, want) {
			t.Fatalf("expected error to list valid value %q, got: %s", want, argumentError.ExpectedType)
		}
	}
}

// Verifies that a string property without an enum passes any value through unchanged.
func TestConvertValuePassesThroughStringWithoutEnum(t *testing.T) {
	property := clicore.ToolProperty{Type: "string"}

	converted, err := convertValue("anything", property, "--label")
	if err != nil {
		t.Fatalf("expected no error, got: %v", err)
	}
	if converted != "anything" {
		t.Fatalf("expected converted value %q, got %q", "anything", converted)
	}
}
