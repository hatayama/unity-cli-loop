package dispatcher

import "os"

// killUnityProcessWithFallback stops Unity by its whole process tree, falling back to killing the
// Editor process alone when the tree kill cannot be carried out. A restart must not become
// impossible just because the platform's tool for killing a tree is unavailable.
func killUnityProcessWithFallback(pid int, killProcessTree func(int) error, killProcess func(int) error) error {
	if err := killProcessTree(pid); err == nil {
		return nil
	}
	return killProcess(pid)
}

// killProcessById stops one process, leaving anything it spawned running.
func killProcessById(pid int) error {
	process, err := os.FindProcess(pid)
	if err != nil {
		return err
	}
	return process.Kill()
}
