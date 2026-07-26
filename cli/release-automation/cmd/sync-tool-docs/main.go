package main

import (
	"flag"
	"os"

	"github.com/hatayama/unity-cli-loop/tools/release-automation/internal/automation"
)

func main() {
	repositoryRoot := flag.String("repository-root", ".", "repository root holding the Unity package and the tool catalog")
	checkOnly := flag.Bool("check", false, "verify the catalog matches the skill parameter tables instead of writing it")
	flag.Parse()

	os.Exit(automation.RunSyncToolDocs(os.Stdout, os.Stderr, automation.SyncToolDocsConfig{
		RepositoryRoot: *repositoryRoot,
		CheckOnly:      *checkOnly,
	}))
}
