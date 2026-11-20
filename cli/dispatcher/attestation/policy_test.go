package attestation

import "testing"

func TestDispatcherPublishIdentityUsesApprovedReleasePolicy(t *testing.T) {
	// Verifies every stamp consumer receives the exact dispatcher release identity enforced at runtime.
	identity := DispatcherPublishIdentity()

	if identity.RepositoryID != ReleaseRepositoryID || identity.RepositoryOwnerID != ReleaseRepositoryOwnerID {
		t.Fatalf("repository mismatch: %s / %s", identity.RepositoryID, identity.RepositoryOwnerID)
	}
	if identity.WorkflowPath != DispatcherPublishWorkflowPath {
		t.Fatalf("workflow mismatch: %s", identity.WorkflowPath)
	}
	if len(identity.Refs) != 2 || identity.Refs[0] != V3BetaBranchRef || identity.Refs[1] != MainBranchRef {
		t.Fatalf("unexpected allowed refs: %v", identity.Refs)
	}
}
