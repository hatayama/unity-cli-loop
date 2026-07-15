package project

import (
	"errors"
	"io/fs"
	"testing"

	clierrors "github.com/hatayama/unity-cli-loop/common/errors"
)

const (
	testUnixParentPath    = "/tmp"
	testUnixEndpointPath  = "/tmp/uloop-501"
	testEffectiveUserID   = uint32(501)
	testRootUserID        = uint32(0)
	testParentPermissions = uint32(0o1777)
)

// Verifies a symlinked /tmp spelling is accepted when stat follows it to a root-owned sticky directory.
func TestValidateUnixEndpointPathsAllowsSecureSymlinkedParent(t *testing.T) {
	reader := secureUnixMetadataReader()
	reader.noFollow[testUnixParentPath] = unixFileMetadata{
		Kind:        unixFileKindSymbolicLink,
		OwnerUserID: testRootUserID,
		Permissions: 0o777,
	}

	err := validateUnixEndpointPaths(
		testUnixParentPath,
		testUnixEndpointPath,
		testEffectiveUserID,
		reader,
	)
	if err != nil {
		t.Fatalf("expected secure symlinked parent to pass: %v", err)
	}
}

// Verifies a missing endpoint directory is a typed not-running state rather than a security acceptance.
func TestValidateUnixEndpointPathsReturnsTypedMissingEndpointError(t *testing.T) {
	reader := secureUnixMetadataReader()
	delete(reader.noFollow, testUnixEndpointPath)

	err := validateUnixEndpointPaths(
		testUnixParentPath,
		testUnixEndpointPath,
		testEffectiveUserID,
		reader,
	)

	var missingErr clierrors.UnityEndpointNotCreatedError
	if !errors.As(err, &missingErr) {
		t.Fatalf("expected typed missing endpoint error, got %T: %v", err, err)
	}
}

// Verifies a followed parent without the sticky bit is rejected.
func TestValidateUnixEndpointPathsRejectsNonStickyResolvedParent(t *testing.T) {
	reader := secureUnixMetadataReader()
	reader.follow[testUnixParentPath] = unixFileMetadata{
		Kind:        unixFileKindDirectory,
		OwnerUserID: testRootUserID,
		Permissions: 0o777,
	}

	err := validateUnixEndpointPaths(
		testUnixParentPath,
		testUnixEndpointPath,
		testEffectiveUserID,
		reader,
	)

	if err == nil {
		t.Fatal("expected non-sticky parent rejection")
	}
}

// Verifies an endpoint-directory symlink is rejected without following it.
func TestValidateUnixEndpointPathsRejectsEndpointSymlink(t *testing.T) {
	reader := secureUnixMetadataReader()
	reader.noFollow[testUnixEndpointPath] = unixFileMetadata{
		Kind:        unixFileKindSymbolicLink,
		OwnerUserID: testEffectiveUserID,
		Permissions: 0o700,
	}

	err := validateUnixEndpointPaths(
		testUnixParentPath,
		testUnixEndpointPath,
		testEffectiveUserID,
		reader,
	)

	if err == nil {
		t.Fatal("expected endpoint symlink rejection")
	}
}

// Verifies an endpoint directory owned by another OS user is rejected.
func TestValidateUnixEndpointPathsRejectsWrongOwner(t *testing.T) {
	reader := secureUnixMetadataReader()
	reader.noFollow[testUnixEndpointPath] = unixFileMetadata{
		Kind:        unixFileKindDirectory,
		OwnerUserID: testEffectiveUserID + 1,
		Permissions: 0o700,
	}

	err := validateUnixEndpointPaths(
		testUnixParentPath,
		testUnixEndpointPath,
		testEffectiveUserID,
		reader,
	)

	if err == nil {
		t.Fatal("expected endpoint owner rejection")
	}
}

// Verifies any group or other permission on the endpoint directory is rejected.
func TestValidateUnixEndpointPathsRejectsNonPrivateMode(t *testing.T) {
	reader := secureUnixMetadataReader()
	reader.noFollow[testUnixEndpointPath] = unixFileMetadata{
		Kind:        unixFileKindDirectory,
		OwnerUserID: testEffectiveUserID,
		Permissions: 0o770,
	}

	err := validateUnixEndpointPaths(
		testUnixParentPath,
		testUnixEndpointPath,
		testEffectiveUserID,
		reader,
	)

	if err == nil {
		t.Fatal("expected endpoint mode rejection")
	}
}

type fakeUnixMetadataReader struct {
	noFollow map[string]unixFileMetadata
	follow   map[string]unixFileMetadata
}

func secureUnixMetadataReader() *fakeUnixMetadataReader {
	return &fakeUnixMetadataReader{
		noFollow: map[string]unixFileMetadata{
			testUnixParentPath: {
				Kind:        unixFileKindDirectory,
				OwnerUserID: testRootUserID,
				Permissions: testParentPermissions,
			},
			testUnixEndpointPath: {
				Kind:        unixFileKindDirectory,
				OwnerUserID: testEffectiveUserID,
				Permissions: 0o700,
			},
		},
		follow: map[string]unixFileMetadata{
			testUnixParentPath: {
				Kind:        unixFileKindDirectory,
				OwnerUserID: testRootUserID,
				Permissions: testParentPermissions,
			},
		},
	}
}

func (reader *fakeUnixMetadataReader) Lstat(path string) (unixFileMetadata, error) {
	metadata, exists := reader.noFollow[path]
	if !exists {
		return unixFileMetadata{}, fs.ErrNotExist
	}
	return metadata, nil
}

func (reader *fakeUnixMetadataReader) Stat(path string) (unixFileMetadata, error) {
	metadata, exists := reader.follow[path]
	if !exists {
		return unixFileMetadata{}, errors.New("missing followed metadata")
	}
	return metadata, nil
}
