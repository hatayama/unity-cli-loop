package attestation

import (
	"encoding/json"
	"testing"
	"time"
)

// Verifies the embedded Sigstore trusted_root.json parses into a usable
// TrustedMaterial so a corrupted commit is caught before it can silently
// break self-update verification.
func TestLoadEmbeddedTrustedMaterial_Parses(t *testing.T) {
	tr, err := LoadEmbeddedTrustedMaterial()
	if err != nil {
		t.Fatalf("expected embedded trusted_root to parse, got: %v", err)
	}
	if tr == nil {
		t.Fatal("expected non-nil trusted material")
	}
	if len(tr.FulcioCertificateAuthorities()) == 0 {
		t.Fatal("expected at least one Fulcio certificate authority")
	}
	if len(tr.RekorLogs()) == 0 {
		t.Fatal("expected at least one Rekor log")
	}
}

// Sigstore public-good roots rotate authorities and transparency-log keys
// periodically. When every embedded authority is about to expire, offline
// verification will start failing en masse. This test fails first — in CI —
// so a maintainer can refresh trusted_root.json (via
// cli/dispatcher/cmd/refresh-attestation-trusted-root) and ship it in a
// dispatcher release before customers notice.
//
// Rule: for each category (Fulcio CAs, Rekor logs, CT logs, TSAs) that is
// present in the root, at least one authority must be usable more than the
// refresh warning horizon into the future — either its validFor.end is set
// beyond that horizon or it has no end (still active indefinitely).
const trustedRootWarningHorizon = 90 * 24 * time.Hour

type validFor struct {
	Start string `json:"start"`
	End   string `json:"end,omitempty"`
}

type rawTLog struct {
	PublicKey struct {
		ValidFor validFor `json:"validFor"`
	} `json:"publicKey"`
}

type rawTrustedRoot struct {
	CertificateAuthorities []struct {
		ValidFor validFor `json:"validFor"`
	} `json:"certificateAuthorities"`
	TimestampAuthorities []struct {
		ValidFor validFor `json:"validFor"`
	} `json:"timestampAuthorities"`
	TLogs  []rawTLog `json:"tlogs"`
	CTLogs []rawTLog `json:"ctlogs"`
}

func TestEmbeddedTrustedRoot_HasFutureCapableAuthorityPerCategory(t *testing.T) {
	var root rawTrustedRoot
	if err := json.Unmarshal(EmbeddedTrustedRootBytes(), &root); err != nil {
		t.Fatalf("parse embedded trusted_root: %v", err)
	}

	now := time.Now().UTC()
	horizon := now.Add(trustedRootWarningHorizon)

	assertHorizon := func(category string, entries []validFor) {
		if len(entries) == 0 {
			return // category simply not populated in this root
		}
		for _, entry := range entries {
			if isValidBeyond(t, entry, horizon) {
				return
			}
		}
		t.Fatalf("no %s authority is usable past %s (all end before). Refresh cli/dispatcher/internal/attestation/trusted_root.json via `go run ./cmd/refresh-attestation-trusted-root` and land a fix(dispatcher) release.", category, horizon.Format(time.RFC3339))
	}

	caEntries := make([]validFor, 0, len(root.CertificateAuthorities))
	for _, e := range root.CertificateAuthorities {
		caEntries = append(caEntries, e.ValidFor)
	}
	tsaEntries := make([]validFor, 0, len(root.TimestampAuthorities))
	for _, e := range root.TimestampAuthorities {
		tsaEntries = append(tsaEntries, e.ValidFor)
	}
	tlogEntries := make([]validFor, 0, len(root.TLogs))
	for _, e := range root.TLogs {
		tlogEntries = append(tlogEntries, e.PublicKey.ValidFor)
	}
	ctlogEntries := make([]validFor, 0, len(root.CTLogs))
	for _, e := range root.CTLogs {
		ctlogEntries = append(ctlogEntries, e.PublicKey.ValidFor)
	}

	assertHorizon("Fulcio CA", caEntries)
	assertHorizon("Timestamp Authority", tsaEntries)
	assertHorizon("Rekor log", tlogEntries)
	assertHorizon("CT log", ctlogEntries)
}

// isValidBeyond returns true when the entry is either unlimited (no end date)
// or its end date is on/after the given horizon. The Sigstore public-good
// root ships historical authorities with a Start value in the past, so we
// only gate on End here — a Start-in-the-future entry has never appeared in
// this root and is not part of the horizon contract this test enforces.
func isValidBeyond(t *testing.T, v validFor, horizon time.Time) bool {
	t.Helper()
	if v.End == "" {
		return true
	}
	end, err := time.Parse(time.RFC3339, v.End)
	if err != nil {
		// Some sigstore roots use millisecond precision. Try that too.
		end, err = time.Parse("2006-01-02T15:04:05.999Z07:00", v.End)
		if err != nil {
			t.Fatalf("cannot parse validFor.end %q: %v", v.End, err)
		}
	}
	return !end.Before(horizon)
}
