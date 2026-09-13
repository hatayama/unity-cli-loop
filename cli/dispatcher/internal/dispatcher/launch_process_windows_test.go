//go:build windows

package dispatcher

import "testing"

// Verifies killing a process id that no longer exists reports an error instead of panicking,
// so a restart whose Editor already exited fails cleanly through both the taskkill path and
// the direct-kill fallback. The process-tree behavior itself only shows on a real Editor.
func TestKillUnityProcessReportsMissingProcess(t *testing.T) {
	const unusedProcessId int = 0x7FFFFFF0

	if err := killUnityProcess(unusedProcessId); err == nil {
		t.Fatal("killing a process id that does not exist must report an error")
	}
}
