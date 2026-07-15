//go:build !windows

package ipcendpoint

import (
	"errors"
	"fmt"
	"io/fs"
	"os"
	"path/filepath"
	"syscall"
)

const (
	unixSocketParent                = "/tmp"
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
	Lstat(string) (unixFileMetadata, error)
	Stat(string) (unixFileMetadata, error)
}

// Validate preserves the private Unix endpoint contract immediately before a client dials.
func Validate(network string, address string) error {
	if network != "unix" {
		return nil
	}
	return validateUnixEndpointPaths(unixSocketParent, filepath.Dir(address), uint32(os.Geteuid()), osUnixMetadataReader{})
}

func validateUnixEndpointPaths(parentPath string, endpointDirectoryPath string, effectiveUserID uint32, reader unixMetadataReader) error {
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
	if parentFollowed.Kind != unixFileKindDirectory || parentFollowed.OwnerUserID != 0 || parentFollowed.Permissions&unixStickyBit == 0 {
		return fmt.Errorf("resolved Unix endpoint parent %s must be a root-owned sticky directory", parentPath)
	}
	endpointDirectory, err := reader.Lstat(endpointDirectoryPath)
	if err != nil {
		if errors.Is(err, fs.ErrNotExist) {
			return UnityEndpointNotCreatedError{EndpointDirectory: endpointDirectoryPath}
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

type osUnixMetadataReader struct{}

func (osUnixMetadataReader) Lstat(path string) (unixFileMetadata, error) {
	info, err := os.Lstat(path)
	if err != nil {
		return unixFileMetadata{}, err
	}
	return unixMetadataFromFileInfo(info)
}

func (osUnixMetadataReader) Stat(path string) (unixFileMetadata, error) {
	info, err := os.Stat(path)
	if err != nil {
		return unixFileMetadata{}, err
	}
	return unixMetadataFromFileInfo(info)
}

func unixMetadataFromFileInfo(info os.FileInfo) (unixFileMetadata, error) {
	stat, ok := info.Sys().(*syscall.Stat_t)
	if !ok {
		return unixFileMetadata{}, fmt.Errorf("unix metadata for %s has unexpected type %T", info.Name(), info.Sys())
	}
	permissions := uint32(info.Mode().Perm())
	if info.Mode()&os.ModeSetuid != 0 {
		permissions |= 0o4000
	}
	if info.Mode()&os.ModeSetgid != 0 {
		permissions |= 0o2000
	}
	if info.Mode()&os.ModeSticky != 0 {
		permissions |= unixStickyBit
	}
	return unixFileMetadata{Kind: unixFileKindFromMode(info.Mode()), OwnerUserID: stat.Uid, Permissions: permissions}, nil
}

func unixFileKindFromMode(mode os.FileMode) unixFileKind {
	if mode&os.ModeSymlink != 0 {
		return unixFileKindSymbolicLink
	}
	if mode.IsDir() {
		return unixFileKindDirectory
	}
	if mode&os.ModeSocket != 0 {
		return unixFileKindSocket
	}
	return unixFileKindOther
}
