//go:build !windows

package project

import (
	"os"
	"syscall"
	"testing"
	"time"
)

// Verifies the OS metadata adapter reports real owner and permission bits for a private directory.
func TestOSUnixMetadataReaderReportsRealOwnerAndMode(t *testing.T) {
	directoryPath, err := os.MkdirTemp(unixSocketParent, "uloop-security-test-")
	if err != nil {
		t.Fatalf("create private Unix test directory: %v", err)
	}
	t.Cleanup(func() {
		if err := os.Remove(directoryPath); err != nil {
			t.Errorf("remove private Unix test directory: %v", err)
		}
	})

	metadata, err := (osUnixMetadataReader{}).Lstat(directoryPath)
	if err != nil {
		t.Fatalf("lstat private Unix test directory: %v", err)
	}
	if metadata.Kind != unixFileKindDirectory {
		t.Fatalf("expected directory kind, got %d", metadata.Kind)
	}
	if metadata.OwnerUserID != uint32(os.Geteuid()) {
		t.Fatalf("expected owner %d, got %d", os.Geteuid(), metadata.OwnerUserID)
	}
	if metadata.Permissions != unixPrivateDirectoryPermissions {
		t.Fatalf("expected mode 0700, got %#o", metadata.Permissions)
	}
}

// Verifies the real /tmp path may be a symlink while its followed target remains root-owned and sticky.
func TestOSUnixMetadataReaderAcceptsHostTmpSymlinkBoundary(t *testing.T) {
	reader := osUnixMetadataReader{}
	endpointDirectoryPath, err := os.MkdirTemp(unixSocketParent, "uloop-security-policy-test-")
	if err != nil {
		t.Fatalf("create endpoint policy directory: %v", err)
	}
	t.Cleanup(func() {
		if err := os.Remove(endpointDirectoryPath); err != nil {
			t.Errorf("remove endpoint policy directory: %v", err)
		}
	})

	err = validateUnixEndpointPaths(
		unixSocketParent,
		endpointDirectoryPath,
		uint32(os.Geteuid()),
		reader,
	)
	if err != nil {
		t.Fatalf("expected host /tmp security boundary to pass: %v", err)
	}
}

// Verifies setuid and setgid bits remain visible to the exact 0700 policy comparison.
func TestUnixMetadataFromFileInfoPreservesSetIDBits(t *testing.T) {
	info := fakeUnixFileInfo{
		mode: os.FileMode(0o700) | os.ModeDir | os.ModeSetuid | os.ModeSetgid,
		uid:  uint32(os.Geteuid()),
	}

	metadata, err := unixMetadataFromFileInfo(info)
	if err != nil {
		t.Fatalf("convert Unix metadata: %v", err)
	}
	if metadata.Permissions != 0o6700 {
		t.Fatalf("expected set-ID permission bits, got %#o", metadata.Permissions)
	}
}

type fakeUnixFileInfo struct {
	mode os.FileMode
	uid  uint32
}

func (info fakeUnixFileInfo) Name() string       { return "endpoint" }
func (info fakeUnixFileInfo) Size() int64        { return 0 }
func (info fakeUnixFileInfo) Mode() os.FileMode  { return info.mode }
func (info fakeUnixFileInfo) ModTime() time.Time { return time.Time{} }
func (info fakeUnixFileInfo) IsDir() bool        { return info.mode.IsDir() }
func (info fakeUnixFileInfo) Sys() any           { return &syscall.Stat_t{Uid: info.uid} }
