//go:build !windows

package project

import (
	"fmt"
	"os"
	"syscall"

	"github.com/hatayama/unity-cli-loop/common/unityipc"
)

type platformEndpointDirectoryValidator struct{}

func (platformEndpointDirectoryValidator) Validate(endpoint unityipc.Endpoint) error {
	if endpoint.Network != "unix" {
		return nil
	}

	return validateUnixEndpointPaths(
		unixSocketParent,
		unixEndpointDirectoryPath(endpoint),
		uint32(os.Geteuid()),
		osUnixMetadataReader{},
	)
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

	return unixFileMetadata{
		Kind:        unixFileKindFromMode(info.Mode()),
		OwnerUserID: stat.Uid,
		Permissions: permissions,
	}, nil
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
