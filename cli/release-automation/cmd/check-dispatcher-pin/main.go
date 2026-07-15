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

func main() {
	network := flag.Bool("network", false, "verify the published Release attestation subjects")
	baseRef := flag.String("base", "", "base git ref used to select network verification")
	headRef := flag.String("head", "HEAD", "head git ref used to select network verification")
	flag.Parse()
	packagePinPath := filepath.Join("..", "..", "Packages", "src", "project-runner-pin.json")
	projectPinPath := filepath.Join("..", "..", ".uloop", "project-runner-pin.json")
	packagePin, err := os.ReadFile(packagePinPath)
	if err == nil {
		projectPin, projectErr := os.ReadFile(projectPinPath)
		if projectErr != nil {
			err = projectErr
		} else {
			err = automation.ValidateDispatcherPinOffline(packagePin, projectPin)
		}
	}
	if err != nil {
		fmt.Fprintln(os.Stderr, "check-dispatcher-pin:", err)
		os.Exit(1)
	}
	warnIfDispatcherInstallerScriptsDrift(packagePin)
	shouldVerifyNetwork := *network
	if *baseRef != "" {
		shouldVerifyNetwork = dispatcherPinNetworkVerificationRequired(*baseRef, *headRef)
	}
	if shouldVerifyNetwork {
		ctx, cancel := context.WithTimeout(context.Background(), 2*time.Minute)
		err = automation.VerifyDispatcherPinSubjects(ctx, packagePin)
		cancel()
		if err != nil {
			fmt.Fprintln(os.Stderr, "check-dispatcher-pin:", err)
			os.Exit(1)
		}
	}
	if !shouldVerifyNetwork {
		fmt.Println("Dispatcher pin network verification skipped because no watched paths changed.")
	}
	fmt.Println("Dispatcher pin guard passed.")
}

func warnIfDispatcherInstallerScriptsDrift(packagePin []byte) {
	scripts := map[string][]byte{}
	for _, scriptName := range []string{"install.sh", "install.ps1"} {
		scriptPath := filepath.Join("..", "..", "scripts", scriptName)
		scriptData, err := os.ReadFile(scriptPath)
		if err != nil {
			fmt.Fprintln(os.Stderr, "check-dispatcher-pin: unable to compare installer source drift:", err)
			return
		}
		scripts[scriptName] = scriptData
	}
	warnings, err := automation.DispatcherPinScriptDriftWarnings(packagePin, scripts)
	if err != nil {
		fmt.Fprintln(os.Stderr, "check-dispatcher-pin: unable to compare installer source drift:", err)
		return
	}
	for _, warning := range warnings {
		fmt.Fprintln(os.Stderr, "check-dispatcher-pin: review warning:", warning)
	}
}

func dispatcherPinNetworkVerificationRequired(baseRef string, headRef string) bool {
	command := exec.Command("git", "diff", "--name-only", baseRef+".."+headRef)
	output, err := command.Output()
	if err != nil {
		fmt.Fprintln(os.Stderr, "check-dispatcher-pin: git diff unavailable; running network verification:", err)
		return true
	}
	for _, path := range strings.Fields(string(output)) {
		if dispatcherPinPathNeedsNetworkVerification(path) {
			return true
		}
	}
	return false
}

func dispatcherPinPathNeedsNetworkVerification(path string) bool {
	return path == "Packages/src/project-runner-pin.json" ||
		path == ".uloop/project-runner-pin.json" ||
		path == "scripts/install.sh" ||
		path == "scripts/install.ps1" ||
		path == ".github/workflows/build-and-test.yml" ||
		strings.HasPrefix(path, "cli/dispatcher/attestation/") ||
		strings.HasPrefix(path, "cli/release-automation/")
}
