package unityprocess

// shouldActivateViaOsascriptMac reports whether open -a is unsafe for this process.
// matchingProcessCount 0 means the count could not be determined, so PID-based
// osascript is the only way to avoid activating the wrong Unity instance.
func shouldActivateViaOsascriptMac(bundlePath string, matchingProcessCount int) bool {
	return bundlePath == "" || matchingProcessCount != 1
}
