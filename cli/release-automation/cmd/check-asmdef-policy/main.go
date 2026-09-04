// check-asmdef-policy fails when an assembly definition under Packages/src
// references another assembly in a direction the package architecture forbids
// (tool to tool, shared tool utilities to Application or above, or a layer
// referencing an outer layer). Tolerated references are listed with a reason in
// tools/asmdef-policy-allowlist.json; an entry whose reference no longer exists
// also fails so the allowlist shrinks as debts are repaid.
package main

import (
	"flag"
	"os"

	"github.com/hatayama/unity-cli-loop/tools/release-automation/internal/automation"
)

func main() {
	root := flag.String("root", ".", "repository root to scan")
	allowlist := flag.String("allowlist", "", "allowlist path (default: <root>/"+automation.DefaultAsmdefPolicyAllowlistPath+")")
	flag.Parse()

	os.Exit(automation.RunAsmdefPolicyCheck(os.Stdout, os.Stderr, automation.AsmdefPolicyCheckOptions{
		Root:          *root,
		AllowlistPath: *allowlist,
	}))
}
