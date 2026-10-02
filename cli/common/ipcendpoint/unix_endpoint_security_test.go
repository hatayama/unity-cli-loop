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

// erroringUnixMetadataReader returns configured errors per path before falling back to secure metadata.
type erroringUnixMetadataReader struct {
	*fakeUnixMetadataReader
	lstatErrors map[string]error
	statErrors  map[string]error
}

func (r *erroringUnixMetadataReader) Lstat(path string) (unixFileMetadata, error) {
	if err, ok := r.lstatErrors[path]; ok {
		return unixFileMetadata{}, err
	}
	return r.fakeUnixMetadataReader.Lstat(path)
}

func (r *erroringUnixMetadataReader) Stat(path string) (unixFileMetadata, error) {
	if err, ok := r.statErrors[path]; ok {
		return unixFileMetadata{}, err
	}
	return r.fakeUnixMetadataReader.Stat(path)
}

// Verifies each validation step reports its own failure, so an inspection error or an unexpected parent
// kind is never mistaken for a missing endpoint or a later policy check.
func TestValidateUnixEndpointPathsReportsEachFailingStep(t *testing.T) {
	inspectErr := errors.New("inspect failed")
	tests := []struct {
		name            string
		configure       func(*erroringUnixMetadataReader)
		expectedMessage string
	}{
		{"parent lstat error", func(r *erroringUnixMetadataReader) {
			r.lstatErrors[testUnixParentPath] = inspectErr
		}, "inspect Unix endpoint parent without following links: inspect failed"},
		{"parent is a socket", func(r *erroringUnixMetadataReader) {
			r.noFollow[testUnixParentPath] = unixFileMetadata{Kind: unixFileKindSocket, OwnerUserID: 0, Permissions: 0o1777}
		}, "unix endpoint parent /tmp is neither a directory nor a symbolic link"},
		{"parent stat error", func(r *erroringUnixMetadataReader) {
			r.statErrors[testUnixParentPath] = inspectErr
		}, "inspect resolved Unix endpoint parent: inspect failed"},
		{"resolved parent not a directory", func(r *erroringUnixMetadataReader) {
			r.follow[testUnixParentPath] = unixFileMetadata{Kind: unixFileKindOther, OwnerUserID: 0, Permissions: 0o1777}
		}, "resolved Unix endpoint parent /tmp must be a root-owned sticky directory"},
		{"parent not root owned", func(r *erroringUnixMetadataReader) {
			r.follow[testUnixParentPath] = unixFileMetadata{Kind: unixFileKindDirectory, OwnerUserID: testEffectiveUserID, Permissions: 0o1777}
		}, "resolved Unix endpoint parent /tmp must be a root-owned sticky directory"},
		{"endpoint lstat error", func(r *erroringUnixMetadataReader) {
			r.lstatErrors[testUnixEndpointPath] = inspectErr
		}, "inspect Unix endpoint directory: inspect failed"},
		{"endpoint symlink", func(r *erroringUnixMetadataReader) {
			r.noFollow[testUnixEndpointPath] = unixFileMetadata{Kind: unixFileKindSymbolicLink, OwnerUserID: testEffectiveUserID, Permissions: 0o700}
		}, "unix endpoint directory /tmp/uloop-501 must be a real directory"},
		{"endpoint owner", func(r *erroringUnixMetadataReader) {
			r.noFollow[testUnixEndpointPath] = unixFileMetadata{Kind: unixFileKindDirectory, OwnerUserID: testEffectiveUserID + 1, Permissions: 0o700}
		}, "unix endpoint directory /tmp/uloop-501 is not owned by the current user"},
		{"endpoint mode", func(r *erroringUnixMetadataReader) {
			r.noFollow[testUnixEndpointPath] = unixFileMetadata{Kind: unixFileKindDirectory, OwnerUserID: testEffectiveUserID, Permissions: 0o770}
		}, "unix endpoint directory /tmp/uloop-501 must have mode 0700"},
	}
	for _, test := range tests {
		t.Run(test.name, func(t *testing.T) {
			reader := &erroringUnixMetadataReader{
				fakeUnixMetadataReader: secureUnixMetadataReader(),
				lstatErrors:            map[string]error{},
				statErrors:             map[string]error{},
			}
			test.configure(reader)

			err := validateUnixEndpointPaths(testUnixParentPath, testUnixEndpointPath, testEffectiveUserID, reader)

			if err == nil || err.Error() != test.expectedMessage {
				t.Fatalf("validation error = %v, want %q", err, test.expectedMessage)
			}
		})
	}
}
