package project

import (
	"errors"
	"fmt"
	"io/fs"
	"path/filepath"

	clierrors "github.com/hatayama/unity-cli-loop/common/errors"
	"github.com/hatayama/unity-cli-loop/common/unityipc"
)

const (
	unixPrivateDirectoryPermissions = uint32(0o700)
	unixStickyBit                   = uint32(0o1000)
)

type unixFileKind int

const (
	unixFileKindOther unixFileKind = iota
	unixFileKindDirectory
	unixFileKindSymbolicLink
	unixFileKindSocket
)

type unixFileMetadata struct {
	Kind        unixFileKind
	OwnerUserID uint32
	Permissions uint32
}

type unixMetadataReader interface {
	Lstat(path string) (unixFileMetadata, error)
	Stat(path string) (unixFileMetadata, error)
}

type endpointDirectoryValidator interface {
	Validate(endpoint unityipc.Endpoint) error
}

func validateUnixEndpointPaths(
	parentPath string,
	endpointDirectoryPath string,
	effectiveUserID uint32,
	reader unixMetadataReader,
) error {
	parentNoFollow, err := reader.Lstat(parentPath)
	if err != nil {
		return fmt.Errorf("inspect Unix endpoint parent without following links: %w", err)
	}
	if parentNoFollow.Kind != unixFileKindDirectory && parentNoFollow.Kind != unixFileKindSymbolicLink {
		return fmt.Errorf("unix endpoint parent %s is neither a directory nor a symbolic link", parentPath)
	}

	parentFollowed, err := reader.Stat(parentPath)
	if err != nil {
		return fmt.Errorf("inspect resolved Unix endpoint parent: %w", err)
	}
	if parentFollowed.Kind != unixFileKindDirectory ||
		parentFollowed.OwnerUserID != 0 ||
		parentFollowed.Permissions&unixStickyBit == 0 {
		return fmt.Errorf("resolved Unix endpoint parent %s must be a root-owned sticky directory", parentPath)
	}

	endpointDirectory, err := reader.Lstat(endpointDirectoryPath)
	if err != nil {
		if errors.Is(err, fs.ErrNotExist) {
			return clierrors.UnityEndpointNotCreatedError{
				EndpointDirectory: endpointDirectoryPath,
			}
		}
		return fmt.Errorf("inspect Unix endpoint directory: %w", err)
	}
	if endpointDirectory.Kind != unixFileKindDirectory {
		return fmt.Errorf("unix endpoint directory %s must be a real directory", endpointDirectoryPath)
	}
	if endpointDirectory.OwnerUserID != effectiveUserID {
		return fmt.Errorf("unix endpoint directory %s is not owned by the current user", endpointDirectoryPath)
	}
	if endpointDirectory.Permissions != unixPrivateDirectoryPermissions {
		return fmt.Errorf("unix endpoint directory %s must have mode 0700", endpointDirectoryPath)
	}

	return nil
}

func unixEndpointDirectoryPath(endpoint unityipc.Endpoint) string {
	return filepath.Dir(endpoint.Address)
}
