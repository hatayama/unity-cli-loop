package attestation

import (
	"testing"

	"github.com/sigstore/sigstore-go/pkg/fulcio/certificate"
	"github.com/sigstore/sigstore-go/pkg/verify"
)

const (
	testReleaseCommitSHA = "0123456789abcdef0123456789abcdef01234567"
	// Fulcio wrote these into every published release certificate of this repository; they are
	// GitHub's numeric IDs, which a repository rename does not change.
	certifiedRepositoryID      = "1002447118"
	certifiedRepositoryOwnerID = "842587"
)

// releaseCertificateSummary describes the certificate dispatcher-publish.yml on main receives
// while the repository is called repositoryName.
func releaseCertificateSummary(repositoryName string) certificate.Summary {
	return certificate.Summary{
		SubjectAlternativeName: "https://github.com/" + repositoryName + "/.github/workflows/dispatcher-publish.yml@refs/heads/main",
		Extensions: certificate.Extensions{
			Issuer:                          "https://token.actions.githubusercontent.com",
			SourceRepositoryURI:             "https://github.com/" + repositoryName,
			SourceRepositoryIdentifier:      certifiedRepositoryID,
			SourceRepositoryOwnerIdentifier: certifiedRepositoryOwnerID,
			SourceRepositoryDigest:          testReleaseCommitSHA,
			SourceRepositoryRef:             "refs/heads/main",
		},
	}
}

func dispatcherReleaseIdentities(t *testing.T) verify.CertificateIdentities {
	t.Helper()
	identities, err := buildCertificateIdentities(DispatcherPublishIdentity(), testReleaseCommitSHA)
	if err != nil {
		t.Fatalf("build identities: %v", err)
	}
	return identities
}

// Verifies the dispatcher release identity accepts this repository's certificates under its
// current name and after a rename, since only the repository's numeric IDs identify it.
func TestDispatcherPublishIdentityAcceptsTheRepositoryUnderAnyName(t *testing.T) {
	identities := dispatcherReleaseIdentities(t)
	for _, repositoryName := range []string{"hatayama/unity-cli-loop", "hatayama/unity-loop-harness"} {
		t.Run(repositoryName, func(t *testing.T) {
			if _, err := identities.Verify(releaseCertificateSummary(repositoryName)); err != nil {
				t.Fatalf("expected the certificate to match, got %v", err)
			}
		})
	}
}

// Verifies the dispatcher release identity rejects a certificate that differs from a matching one
// in a single identity field, so each pinned field is enforced on its own, and rejects another
// repository's reusable workflow called from an unlisted branch of this repository.
func TestDispatcherPublishIdentityRejectsCertificatesFromElsewhere(t *testing.T) {
	identities := dispatcherReleaseIdentities(t)
	cases := []struct {
		name   string
		mutate func(summary *certificate.Summary)
	}{
		{name: "another repository under the same name", mutate: func(summary *certificate.Summary) {
			summary.SourceRepositoryIdentifier = "1"
		}},
		{name: "another owner", mutate: func(summary *certificate.Summary) {
			summary.SourceRepositoryOwnerIdentifier = "2"
		}},
		{name: "another workflow", mutate: func(summary *certificate.Summary) {
			summary.SubjectAlternativeName = "https://github.com/hatayama/unity-cli-loop/.github/workflows/native-cli-publish.yml@refs/heads/main"
		}},
		{name: "an unlisted source ref", mutate: func(summary *certificate.Summary) {
			summary.SourceRepositoryRef = "refs/heads/feature"
		}},
		{name: "another repository's reusable workflow called from an unlisted ref", mutate: func(summary *certificate.Summary) {
			summary.SubjectAlternativeName = "https://github.com/attacker/tools/.github/workflows/dispatcher-publish.yml@refs/heads/main"
			summary.SourceRepositoryRef = "refs/heads/feature"
		}},
		{name: "an unlisted ref", mutate: func(summary *certificate.Summary) {
			summary.SubjectAlternativeName = "https://github.com/hatayama/unity-cli-loop/.github/workflows/dispatcher-publish.yml@refs/heads/feature"
		}},
		{name: "text after the ref", mutate: func(summary *certificate.Summary) {
			summary.SubjectAlternativeName += "-evil"
		}},
		{name: "text before the host", mutate: func(summary *certificate.Summary) {
			summary.SubjectAlternativeName = "https://evil.example/" + summary.SubjectAlternativeName
		}},
		{name: "another commit", mutate: func(summary *certificate.Summary) {
			summary.SourceRepositoryDigest = "1111111111111111111111111111111111111111"
		}},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			summary := releaseCertificateSummary("hatayama/unity-cli-loop")
			testCase.mutate(&summary)
			if _, err := identities.Verify(summary); err == nil {
				t.Fatalf("expected %q to be rejected", summary.SubjectAlternativeName)
			}
		})
	}
}
