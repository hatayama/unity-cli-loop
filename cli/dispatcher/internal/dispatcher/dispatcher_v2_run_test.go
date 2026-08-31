package dispatcher

import "testing"

func TestDispatcherV2ModeNoticeReportsDelegatedPackageAndVersion(t *testing.T) {
	// Verifies the notice names the delegated V2 CLI package and its version so the caller can identify the executed generation.
	notice := dispatcherV2ModeNotice("2.2.0")

	want := "uloop: executing in V2 mode (" + dispatcherV2CLIPackageName + "@2.2.0)\n"
	if notice != want {
		t.Fatalf("notice = %q, want %q", notice, want)
	}
}
