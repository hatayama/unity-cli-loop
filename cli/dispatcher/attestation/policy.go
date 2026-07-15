package attestation

// Release identity constants are exported so every component that authorizes a
// dispatcher release applies the exact policy enforced by the runtime verifier.
const (
	ReleaseRepository                = "hatayama/unity-cli-loop"
	DispatcherPublishWorkflowPath    = ".github/workflows/dispatcher-publish.yml"
	ProjectRunnerPublishWorkflowPath = ".github/workflows/native-cli-publish.yml"
	MainBranchRef                    = "refs/heads/main"
	V3BetaBranchRef                  = "refs/heads/v3-beta"
)

// IdentityForWorkflow returns the closed identity policy for an approved
// release-publishing workflow.
func IdentityForWorkflow(workflowPath string) Identity {
	return Identity{
		Repository:   ReleaseRepository,
		WorkflowPath: workflowPath,
		Refs:         []string{V3BetaBranchRef, MainBranchRef},
	}
}

// DispatcherPublishIdentity returns the identity policy for dispatcher release assets.
func DispatcherPublishIdentity() Identity {
	return IdentityForWorkflow(DispatcherPublishWorkflowPath)
}

// ProjectRunnerPublishIdentity returns the identity policy for project runner release assets.
func ProjectRunnerPublishIdentity() Identity {
	return IdentityForWorkflow(ProjectRunnerPublishWorkflowPath)
}
