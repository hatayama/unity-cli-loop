//go:build !windows

package ipcendpoint

import (
	"errors"
	"io/fs"
	"testing"
)

const (
	testUnixParentPath   = "/tmp"
	testUnixEndpointPath = "/tmp/uloop-501"
	testEffectiveUserID  = uint32(501)
)

// Verifies the contract permits a symlinked /tmp parent only when stat resolves it to root-owned sticky storage.
func TestValidateUnixEndpointPathsAllowsSecureSymlinkedParent(t *testing.T) {
	reader := secureUnixMetadataReader()
	reader.noFollow[testUnixParentPath] = unixFileMetadata{Kind: unixFileKindSymbolicLink, OwnerUserID: 0, Permissions: 0o777}
	if err := validateUnixEndpointPaths(testUnixParentPath, testUnixEndpointPath, testEffectiveUserID, reader); err != nil {
		t.Fatalf("expected secure parent: %v", err)
	}
}

// Verifies a missing private endpoint directory stays a typed not-running state.
func TestValidateUnixEndpointPathsReturnsTypedMissingEndpointError(t *testing.T) {
	reader := secureUnixMetadataReader()
	delete(reader.noFollow, testUnixEndpointPath)
	err := validateUnixEndpointPaths(testUnixParentPath, testUnixEndpointPath, testEffectiveUserID, reader)
	var missing UnityEndpointNotCreatedError
	if !errors.As(err, &missing) {
		t.Fatalf("expected typed missing endpoint, got %T: %v", err, err)
	}
}

// Verifies insecure parent and endpoint metadata fail closed.
func TestValidateUnixEndpointPathsRejectsInsecureFilesystemMetadata(t *testing.T) {
	tests := []struct {
		name      string
		configure func(*fakeUnixMetadataReader)
	}{
		{"non sticky parent", func(r *fakeUnixMetadataReader) {
			r.follow[testUnixParentPath] = unixFileMetadata{Kind: unixFileKindDirectory, OwnerUserID: 0, Permissions: 0o777}
		}},
		{"endpoint symlink", func(r *fakeUnixMetadataReader) {
			r.noFollow[testUnixEndpointPath] = unixFileMetadata{Kind: unixFileKindSymbolicLink, OwnerUserID: testEffectiveUserID, Permissions: 0o700}
		}},
		{"wrong endpoint owner", func(r *fakeUnixMetadataReader) {
			r.noFollow[testUnixEndpointPath] = unixFileMetadata{Kind: unixFileKindDirectory, OwnerUserID: testEffectiveUserID + 1, Permissions: 0o700}
		}},
		{"permissive endpoint mode", func(r *fakeUnixMetadataReader) {
			r.noFollow[testUnixEndpointPath] = unixFileMetadata{Kind: unixFileKindDirectory, OwnerUserID: testEffectiveUserID, Permissions: 0o770}
		}},
	}
	for _, test := range tests {
		t.Run(test.name, func(t *testing.T) {
			reader := secureUnixMetadataReader()
			test.configure(reader)
			if err := validateUnixEndpointPaths(testUnixParentPath, testUnixEndpointPath, testEffectiveUserID, reader); err == nil {
				t.Fatal("expected security rejection")
			}
		})
	}
}

type fakeUnixMetadataReader struct {
	noFollow map[string]unixFileMetadata
	follow   map[string]unixFileMetadata
}

func secureUnixMetadataReader() *fakeUnixMetadataReader {
	return &fakeUnixMetadataReader{
		noFollow: map[string]unixFileMetadata{testUnixParentPath: {Kind: unixFileKindDirectory, OwnerUserID: 0, Permissions: 0o1777}, testUnixEndpointPath: {Kind: unixFileKindDirectory, OwnerUserID: testEffectiveUserID, Permissions: 0o700}},
		follow:   map[string]unixFileMetadata{testUnixParentPath: {Kind: unixFileKindDirectory, OwnerUserID: 0, Permissions: 0o1777}},
	}
}

func (r *fakeUnixMetadataReader) Lstat(path string) (unixFileMetadata, error) {
	v, ok := r.noFollow[path]
	if !ok {
		return unixFileMetadata{}, fs.ErrNotExist
	}
	return v, nil
}

func (r *fakeUnixMetadataReader) Stat(path string) (unixFileMetadata, error) {
	v, ok := r.follow[path]
	if !ok {
		return unixFileMetadata{}, errors.New("missing followed metadata")
	}
	return v, nil
}
