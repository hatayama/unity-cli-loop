package tooldocs

import "testing"

// Verifies that list exposes exactly one CLI-only flag, the boolean --names switch.
func TestListCLIOnlyOptionsDeclaresNamesFlag(t *testing.T) {
	options := ListCLIOnlyOptions()

	if len(options) != 1 {
		t.Fatalf("expected one list option, got %#v", options)
	}
	if options[0].FlagName != ListNamesFlagName || options[0].Type != "boolean" || options[0].Description == "" {
		t.Fatalf("unexpected list option: %#v", options[0])
	}
}
