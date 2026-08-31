// stamp-dispatcher-pin verifies a published dispatcher release and records its
// attested asset digests in the Unity package pin.
package main

import (
	"context"
	"flag"
	"fmt"
	"os"
	"os/exec"
	"path/filepath"
	"strings"
	"time"

	"github.com/hatayama/unity-cli-loop/tools/release-automation/internal/automation"
)

const stampDispatcherPinTimeout = 2 * time.Minute

func main() {
	releaseTag := flag.String("tag", "", "immutable dispatcher Release tag to verify and stamp")
	flag.Parse()
	if *releaseTag == "" || flag.NArg() != 0 {
		fmt.Fprintln(os.Stderr, "usage: stamp-dispatcher-pin --tag dispatcher-vX.Y.Z")
		os.Exit(2)
	}

	ctx, cancel := context.WithTimeout(context.Background(), stampDispatcherPinTimeout)
	defer cancel()
	repositoryRoot, err := repositoryRoot(ctx)
	if err != nil {
		fmt.Fprintln(os.Stderr, "stamp-dispatcher-pin:", err)
		os.Exit(1)
	}
	pinPath := filepath.Join(repositoryRoot, "Packages", "src", "project-runner-pin.json")
	if err := automation.StampDispatcherPin(ctx, pinPath, *releaseTag); err != nil {
		fmt.Fprintln(os.Stderr, "stamp-dispatcher-pin:", err)
		os.Exit(1)
	}
	fmt.Printf("stamped %s from %s\n", pinPath, *releaseTag)
}

func repositoryRoot(ctx context.Context) (string, error) {
	command := exec.CommandContext(ctx, "git", "rev-parse", "--show-toplevel")
	output, err := command.Output()
	if err != nil {
		return "", fmt.Errorf("resolve repository root: %w", err)
	}
	root := strings.TrimSpace(string(output))
	if root == "" {
		return "", fmt.Errorf("git returned an empty repository root")
	}
	return root, nil
}
