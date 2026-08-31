// Refreshes the Sigstore trusted_root.json embedded by cli/dispatcher/attestation.
//
// Run this from the dispatcher module root to fetch the current Sigstore
// public-good trusted_root.json via TUF and write it as
// attestation/trusted_root.json. The dispatcher then embeds that
// file so verification stays offline at runtime. Refresh cadence is
// quarterly; the attestation package has a CI guard test that fails when
// any embedded authority's validity window is within 90 days of expiring,
// so a missed refresh surfaces as a red build rather than a fleet outage.
//
// Runs from module root:
//
//	go run ./cmd/refresh-attestation-trusted-root
package main

import (
	"fmt"
	"os"
	"path/filepath"

	"github.com/sigstore/sigstore-go/pkg/tuf"
)

func main() {
	if err := run(); err != nil {
		fmt.Fprintln(os.Stderr, "refresh-attestation-trusted-root:", err)
		os.Exit(1)
	}
}

func run() error {
	client, err := tuf.DefaultClient()
	if err != nil {
		return fmt.Errorf("create TUF client: %w", err)
	}
	if err := client.Refresh(); err != nil {
		return fmt.Errorf("refresh TUF metadata: %w", err)
	}
	target, err := client.GetTarget("trusted_root.json")
	if err != nil {
		return fmt.Errorf("get trusted_root.json target: %w", err)
	}
	outputPath := filepath.Join("attestation", "trusted_root.json")
	if err := os.WriteFile(outputPath, target, 0o644); err != nil {
		return fmt.Errorf("write %s: %w", outputPath, err)
	}
	fmt.Printf("wrote %d bytes to %s\n", len(target), outputPath)
	return nil
}
