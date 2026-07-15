//go:build !windows

package ipcendpoint

import (
	"os"
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
