package attestation

// Release identity constants are exported so every component that authorizes a
// dispatcher release applies the exact policy enforced by the runtime verifier.
const (
	// ReleaseRepository names the repository in GitHub API and download URLs only.
	// Release certificates are matched by ReleaseRepositoryID instead, so renaming
	// the repository does not stop installed dispatchers from verifying releases.
	ReleaseRepository = "hatayama/unity-cli-loop"
	// ReleaseRepositoryID and ReleaseRepositoryOwnerID are GitHub's numeric IDs for
	// this repository and its owner, as Fulcio records them in every release
	// certificate. A rename keeps them; a repository deleted and recreated under the
	// same name gets a new ID and fails verification.
	ReleaseRepositoryID              = "1002447118"
	ReleaseRepositoryOwnerID         = "842587"
	DispatcherPublishWorkflowPath    = ".github/workflows/dispatcher-publish.yml"
	ProjectRunnerPublishWorkflowPath = ".github/workflows/native-cli-publish.yml"
	MainBranchRef                    = "refs/heads/main"
	V3BetaBranchRef                  = "refs/heads/v3-beta"
)

// IdentityForWorkflow returns the closed identity policy for an approved
// release-publishing workflow.
func IdentityForWorkflow(workflowPath string) Identity {
	return Identity{
		RepositoryID:      ReleaseRepositoryID,
		RepositoryOwnerID: ReleaseRepositoryOwnerID,
		WorkflowPath:      workflowPath,
		Refs:              []string{V3BetaBranchRef, MainBranchRef},
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
