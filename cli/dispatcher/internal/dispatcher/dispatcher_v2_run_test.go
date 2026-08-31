package dispatcher

import "testing"

func TestDispatcherV2ModeNoticeReportsDelegatedPackageAndVersion(t *testing.T) {
	// Verifies the notice names the delegated V2 CLI package, its version, and the dispatcher version behind it.
	notice := dispatcherV2ModeNotice("2.2.0")

	want := "uloop: executing in V2 mode (" + dispatcherV2CLIPackageName + "@2.2.0) via uloop dispatcher " + dispatcherVersion + "\n"
	if notice != want {
		t.Fatalf("notice = %q, want %q", notice, want)
	}
}
