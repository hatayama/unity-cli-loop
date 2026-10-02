//go:build !windows

package ipcendpoint

import (
	"errors"
	"os"
	"path/filepath"
	"strings"
	"syscall"
	"testing"
	"time"
)

// Verifies the live filesystem adapter reads the current user and exact private mode.
func TestOSUnixMetadataReaderReportsRealOwnerAndMode(t *testing.T) {
	directory, err := os.MkdirTemp(unixSocketParent, "uloop-security-test-")
	if err != nil {
		t.Fatalf("create directory: %v", err)
	}
	t.Cleanup(func() {
		if err := os.Remove(directory); err != nil {
			t.Errorf("remove directory: %v", err)
		}
	})
	metadata, err := (osUnixMetadataReader{}).Lstat(directory)
	if err != nil {
		t.Fatalf("lstat directory: %v", err)
	}
	if metadata.Kind != unixFileKindDirectory || metadata.OwnerUserID != uint32(os.Geteuid()) || metadata.Permissions != unixPrivateDirectoryPermissions {
		t.Fatalf("unexpected metadata: %#v", metadata)
	}
}

// Verifies set-ID bits remain visible to the exact 0700 policy comparison.
func TestUnixMetadataFromFileInfoPreservesSetIDBits(t *testing.T) {
	metadata, err := unixMetadataFromFileInfo(fakeUnixFileInfo{mode: os.FileMode(0o700) | os.ModeDir | os.ModeSetuid | os.ModeSetgid, uid: uint32(os.Geteuid())})
	if err != nil {
		t.Fatalf("convert metadata: %v", err)
	}
	if metadata.Permissions != 0o6700 {
		t.Fatalf("expected set-ID bits, got %#o", metadata.Permissions)
	}
}

type fakeUnixFileInfo struct {
	mode os.FileMode
	uid  uint32
}

func (f fakeUnixFileInfo) Name() string       { return "endpoint" }
func (f fakeUnixFileInfo) Size() int64        { return 0 }
func (f fakeUnixFileInfo) Mode() os.FileMode  { return f.mode }
func (f fakeUnixFileInfo) ModTime() time.Time { return time.Time{} }
func (f fakeUnixFileInfo) IsDir() bool        { return f.mode.IsDir() }
func (f fakeUnixFileInfo) Sys() any           { return &syscall.Stat_t{Uid: f.uid} }

// Verifies that only unix endpoints are validated: other networks pass without touching the filesystem.
func TestValidateSkipsNonUnixNetworks(t *testing.T) {
	if err := Validate("pipe", filepath.Join(t.TempDir(), "missing", "endpoint.sock")); err != nil {
		t.Fatalf("non-unix endpoints should not be validated: %v", err)
	}
}

// Verifies that the live validation reports a missing endpoint directory as the typed not-created state
// and accepts a private directory owned by the current user.
func TestValidateChecksTheLiveEndpointDirectory(t *testing.T) {
	parent, err := (osUnixMetadataReader{}).Stat(unixSocketParent)
	if err != nil || parent.OwnerUserID != 0 || parent.Permissions&unixStickyBit == 0 {
		t.Skipf("%s is not a root-owned sticky directory on this host: %#v (err=%v)", unixSocketParent, parent, err)
	}
	endpointDirectory := t.TempDir()
	if err := os.Chmod(endpointDirectory, 0o700); err != nil {
		t.Fatalf("set private mode: %v", err)
	}

	missingDirectory := filepath.Join(endpointDirectory, "missing")
	err = Validate("unix", filepath.Join(missingDirectory, "endpoint.sock"))
	var missing UnityEndpointNotCreatedError
	if !errors.As(err, &missing) || missing.EndpointDirectory != missingDirectory {
		t.Fatalf("expected typed missing endpoint for %s, got %v", missingDirectory, err)
	}
	if err := Validate("unix", filepath.Join(endpointDirectory, "endpoint.sock")); err != nil {
		t.Fatalf("a private endpoint directory should validate: %v", err)
	}
}

// Verifies that metadata without a Unix stat payload is rejected instead of read as owner 0.
func TestUnixMetadataFromFileInfoRejectsForeignSys(t *testing.T) {
	_, err := unixMetadataFromFileInfo(foreignSysFileInfo{fakeUnixFileInfo{mode: os.ModeDir | 0o700}})

	if err == nil || !strings.Contains(err.Error(), "unix metadata for endpoint has unexpected type") {
		t.Fatalf("expected an unexpected-type error, got %v", err)
	}
}

// Verifies the sticky bit is kept and each file mode maps to its own kind.
func TestUnixMetadataFromFileInfoMapsStickyBitAndKinds(t *testing.T) {
	tests := []struct {
		mode         os.FileMode
		expectedKind unixFileKind
		expectedPerm uint32
	}{
		{mode: os.ModeDir | os.ModeSticky | 0o777, expectedKind: unixFileKindDirectory, expectedPerm: 0o1777},
		{mode: os.ModeSymlink | 0o777, expectedKind: unixFileKindSymbolicLink, expectedPerm: 0o777},
		{mode: os.ModeSocket | 0o600, expectedKind: unixFileKindSocket, expectedPerm: 0o600},
		{mode: 0o644, expectedKind: unixFileKindOther, expectedPerm: 0o644},
	}
	for _, test := range tests {
		metadata, err := unixMetadataFromFileInfo(fakeUnixFileInfo{mode: test.mode, uid: 7})
		if err != nil {
			t.Fatalf("convert metadata for %v: %v", test.mode, err)
		}
		if metadata.Kind != test.expectedKind || metadata.Permissions != test.expectedPerm || metadata.OwnerUserID != 7 {
			t.Errorf("metadata for %v = %#v, want kind %v perm %#o", test.mode, metadata, test.expectedKind, test.expectedPerm)
		}
	}
}

type foreignSysFileInfo struct {
	fakeUnixFileInfo
}

func (foreignSysFileInfo) Sys() any { return "not a stat payload" }

// Verifies the live followed-stat adapter surfaces a missing path as not-exist rather than empty metadata.
func TestOSUnixMetadataReaderStatReportsMissingPath(t *testing.T) {
	_, err := (osUnixMetadataReader{}).Stat(filepath.Join(t.TempDir(), "missing"))

	if !errors.Is(err, os.ErrNotExist) {
		t.Fatalf("expected a not-exist error, got %v", err)
	}
}
