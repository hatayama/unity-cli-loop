//go:build !darwin

package dispatcher

import "os/exec"

func newPlatformUnityLaunchCommand(unityPath string, launchArgs []string) *exec.Cmd {
	return exec.Command(unityPath, launchArgs...)
}
