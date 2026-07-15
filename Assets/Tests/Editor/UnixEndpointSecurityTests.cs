using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Infrastructure;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies Unix endpoint directory and socket policy without depending on host ownership.
    /// </summary>
    public class UnixEndpointSecurityTests
    {
        private const string ParentPath = "/tmp";
        private const string EndpointDirectoryPath = "/tmp/uloop-501";
        private const string SocketPath = EndpointDirectoryPath + "/UnityCliLoop-test.sock";
        private const uint EffectiveUserId = 501;

        /// <summary>
        /// Verifies a symlinked /tmp spelling is accepted when its followed target is root-owned and sticky.
        /// </summary>
        [Test]
        public void EnsureEndpointDirectory_WhenParentIsSymlinkToSecureDirectory_Succeeds()
        {
            FakeUnixNativeFileSystem fileSystem = CreateSecureFileSystem();
            fileSystem.SetMetadata(ParentPath, false, Metadata(UnixFileKind.SymbolicLink, 0, 0x1FF));

            UnixEndpointSecurityPolicy policy = new(fileSystem);
            UnixEndpointSecurityResult result = policy.EnsureEndpointDirectory(EndpointDirectoryPath);

            Assert.That(result.Success, Is.True, result.ErrorMessage);
        }

        /// <summary>
        /// Verifies the followed /tmp target must remain a root-owned sticky directory.
        /// </summary>
        [Test]
        public void EnsureEndpointDirectory_WhenResolvedParentIsNotSticky_FailsClosed()
        {
            FakeUnixNativeFileSystem fileSystem = CreateSecureFileSystem();
            fileSystem.SetMetadata(ParentPath, true, Metadata(UnixFileKind.Directory, 0, 0x1FF));

            UnixEndpointSecurityResult result = new UnixEndpointSecurityPolicy(fileSystem)
                .EnsureEndpointDirectory(EndpointDirectoryPath);

            Assert.That(result.Success, Is.False);
        }

        /// <summary>
        /// Verifies an endpoint-directory symlink is rejected even when its target would be secure.
        /// </summary>
        [Test]
        public void EnsureEndpointDirectory_WhenEndpointDirectoryIsSymlink_FailsClosed()
        {
            FakeUnixNativeFileSystem fileSystem = CreateSecureFileSystem();
            fileSystem.SetMetadata(
                EndpointDirectoryPath,
                false,
                Metadata(UnixFileKind.SymbolicLink, EffectiveUserId, 0x1C0));

            UnixEndpointSecurityResult result = new UnixEndpointSecurityPolicy(fileSystem)
                .EnsureEndpointDirectory(EndpointDirectoryPath);

            Assert.That(result.Success, Is.False);
        }

        /// <summary>
        /// Verifies an endpoint directory owned by another OS user is rejected without repair.
        /// </summary>
        [Test]
        public void EnsureEndpointDirectory_WhenOwnerDoesNotMatchEffectiveUser_FailsWithoutChmod()
        {
            FakeUnixNativeFileSystem fileSystem = CreateSecureFileSystem();
            fileSystem.SetMetadata(
                EndpointDirectoryPath,
                false,
                Metadata(UnixFileKind.Directory, EffectiveUserId + 1, 0x1C0));

            UnixEndpointSecurityResult result = new UnixEndpointSecurityPolicy(fileSystem)
                .EnsureEndpointDirectory(EndpointDirectoryPath);

            Assert.That(result.Success, Is.False);
            Assert.That(fileSystem.ChangedModes, Is.Empty);
        }

        /// <summary>
        /// Verifies non-0700 permissions and special mode bits are rejected instead of auto-repaired.
        /// </summary>
        [TestCase(0x1F8u)]
        [TestCase(0x1C7u)]
        [TestCase(0x9C0u)]
        [TestCase(0x5C0u)]
        public void EnsureEndpointDirectory_WhenExistingModeIsNot0700_FailsWithoutChmod(uint mode)
        {
            FakeUnixNativeFileSystem fileSystem = CreateSecureFileSystem();
            fileSystem.SetMetadata(
                EndpointDirectoryPath,
                false,
                Metadata(UnixFileKind.Directory, EffectiveUserId, mode));

            UnixEndpointSecurityResult result = new UnixEndpointSecurityPolicy(fileSystem)
                .EnsureEndpointDirectory(EndpointDirectoryPath);

            Assert.That(result.Success, Is.False);
            Assert.That(fileSystem.ChangedModes, Is.Empty);
        }

        /// <summary>
        /// Verifies a missing endpoint directory is created atomically with a non-widening 0700 mode.
        /// </summary>
        [Test]
        public void EnsureEndpointDirectory_WhenMissing_CreatesWith0700()
        {
            FakeUnixNativeFileSystem fileSystem = CreateSecureFileSystem();
            fileSystem.SetMetadata(EndpointDirectoryPath, false, UnixFileMetadata.Missing());
            fileSystem.MetadataAfterCreate = Metadata(UnixFileKind.Directory, EffectiveUserId, 0x1C0);

            UnixEndpointSecurityResult result = new UnixEndpointSecurityPolicy(fileSystem)
                .EnsureEndpointDirectory(EndpointDirectoryPath);

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(fileSystem.CreatedModes, Is.EqualTo(new[] { 0x1C0u }));
        }

        /// <summary>
        /// Verifies mkdir EEXIST joins the existing-directory validation path without changing permissions.
        /// </summary>
        [Test]
        public void EnsureEndpointDirectory_WhenCreateReportsAlreadyExists_RevalidatesWithoutRepair()
        {
            FakeUnixNativeFileSystem fileSystem = CreateSecureFileSystem();
            fileSystem.SetMetadata(EndpointDirectoryPath, false, UnixFileMetadata.Missing());
            fileSystem.CreateResult = UnixNativeOperationResult.Failure(UnixNativeError.AlreadyExists);
            fileSystem.MetadataAfterCreate = Metadata(UnixFileKind.Directory, EffectiveUserId, 0x1F8);

            UnixEndpointSecurityResult result = new UnixEndpointSecurityPolicy(fileSystem)
                .EnsureEndpointDirectory(EndpointDirectoryPath);

            Assert.That(result.Success, Is.False);
            Assert.That(fileSystem.ChangedModes, Is.Empty);
        }

        /// <summary>
        /// Verifies only an owner-only socket may be removed as a stale endpoint.
        /// </summary>
        [Test]
        public void ValidateStaleSocket_WhenPathIsRegularFile_FailsClosed()
        {
            FakeUnixNativeFileSystem fileSystem = CreateSecureFileSystem();
            fileSystem.SetMetadata(SocketPath, false, Metadata(UnixFileKind.RegularFile, EffectiveUserId, 0x180));

            UnixEndpointSecurityResult result = new UnixEndpointSecurityPolicy(fileSystem)
                .ValidateStaleSocket(SocketPath);

            Assert.That(result.Success, Is.False);
        }

        /// <summary>
        /// Verifies an owner-owned socket left before chmod may be removed after a crash.
        /// </summary>
        [Test]
        public void ValidateStaleSocket_WhenOwnerSocketHasPermissiveMode_Succeeds()
        {
            FakeUnixNativeFileSystem fileSystem = CreateSecureFileSystem();
            fileSystem.SetMetadata(SocketPath, false, Metadata(UnixFileKind.Socket, EffectiveUserId, 0x1ED));

            UnixEndpointSecurityResult result = new UnixEndpointSecurityPolicy(fileSystem)
                .ValidateStaleSocket(SocketPath);

            Assert.That(result.Success, Is.True, result.ErrorMessage);
        }

        /// <summary>
        /// Verifies a freshly bound socket is restricted to 0600 before listener startup succeeds.
        /// </summary>
        [Test]
        public void RestrictSocket_WhenCalled_ChangesModeTo0600()
        {
            FakeUnixNativeFileSystem fileSystem = CreateSecureFileSystem();
            fileSystem.SetMetadata(SocketPath, false, Metadata(UnixFileKind.Socket, EffectiveUserId, 0x1C0));

            UnixEndpointSecurityResult result = new UnixEndpointSecurityPolicy(fileSystem)
                .RestrictSocket(SocketPath);

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(fileSystem.ChangedModes, Is.EqualTo(new[] { 0x180u }));
        }

        /// <summary>
        /// Verifies the native stat ABI reports real uid and mode values from the host filesystem.
        /// </summary>
        [Test]
        public void UnixNativeFileSystem_WhenReadingRealDirectory_ReportsCurrentOwnerAnd0700()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Assert.Ignore("POSIX metadata is unavailable on Windows.");
            }

            UnixNativeFileSystem fileSystem = new();
            string directoryPath = Path.Combine(
                Path.GetTempPath(),
                "uloop-native-stat-test-" + Guid.NewGuid().ToString("N"));
            UnixNativeOperationResult createResult = fileSystem.CreateDirectory(directoryPath, 0x1C0);
            Assert.That(createResult.Success, Is.True, $"mkdir errno {createResult.ErrorCode}");

            try
            {
                UnixFileMetadata metadata = fileSystem.ReadMetadata(
                    directoryPath,
                    followSymbolicLinks: false);
                Assert.That(metadata.ReadSuccess, Is.True, $"lstat errno {metadata.ErrorCode}");
                Assert.That(metadata.Kind, Is.EqualTo(UnixFileKind.Directory));
                Assert.That(metadata.OwnerUserId, Is.EqualTo(fileSystem.GetEffectiveUserId()));
                Assert.That(metadata.Mode, Is.EqualTo(0x1C0));
            }
            finally
            {
                Directory.Delete(directoryPath);
            }
        }

        /// <summary>
        /// Verifies lstat may observe /tmp as a symlink while stat follows it to a root-owned sticky directory.
        /// </summary>
        [Test]
        public void UnixNativeFileSystem_WhenReadingTmp_FollowsOnlyTheParentStatPath()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Assert.Ignore("POSIX metadata is unavailable on Windows.");
            }

            UnixNativeFileSystem fileSystem = new();
            UnixFileMetadata noFollow = fileSystem.ReadMetadata(ParentPath, followSymbolicLinks: false);
            UnixFileMetadata followed = fileSystem.ReadMetadata(ParentPath, followSymbolicLinks: true);

            Assert.That(noFollow.ReadSuccess, Is.True, $"lstat errno {noFollow.ErrorCode}");
            Assert.That(
                noFollow.Kind,
                Is.EqualTo(UnixFileKind.Directory).Or.EqualTo(UnixFileKind.SymbolicLink));
            Assert.That(followed.ReadSuccess, Is.True, $"stat errno {followed.ErrorCode}");
            Assert.That(followed.Kind, Is.EqualTo(UnixFileKind.Directory));
            Assert.That(followed.OwnerUserId, Is.EqualTo(0));
            Assert.That(followed.Mode & 0x200, Is.Not.EqualTo(0));
        }

        /// <summary>
        /// Verifies each supported Editor OS and architecture selects the reviewed stat ABI offsets.
        /// </summary>
        [TestCase(true, false, Architecture.X64, 4, 2, 16, true)]
        [TestCase(true, false, Architecture.Arm64, 4, 2, 16, false)]
        [TestCase(false, true, Architecture.X64, 24, 4, 28, false)]
        [TestCase(false, true, Architecture.Arm64, 16, 4, 24, false)]
        public void UnixStatLayoutResolver_WhenPlatformIsSupported_ReturnsReviewedOffsets(
            bool isMacOS,
            bool isLinux,
            Architecture architecture,
            int expectedModeOffset,
            int expectedModeSize,
            int expectedOwnerUserIdOffset,
            bool expectedDarwinInode64Symbols)
        {
            UnixStatLayoutResult result = UnixStatLayoutResolver.Resolve(
                isMacOS,
                isLinux,
                architecture);

            Assert.That(result.Success, Is.True);
            Assert.That(result.ModeOffset, Is.EqualTo(expectedModeOffset));
            Assert.That(result.ModeSize, Is.EqualTo(expectedModeSize));
            Assert.That(result.OwnerUserIdOffset, Is.EqualTo(expectedOwnerUserIdOffset));
            Assert.That(result.UseDarwinInode64Symbols, Is.EqualTo(expectedDarwinInode64Symbols));
        }

        /// <summary>
        /// Verifies an unreviewed OS or architecture cannot fall through to a nearby stat ABI layout.
        /// </summary>
        [TestCase(false, true, Architecture.X86)]
        [TestCase(false, false, Architecture.X64)]
        public void UnixStatLayoutResolver_WhenPlatformIsUnsupported_FailsClosed(
            bool isMacOS,
            bool isLinux,
            Architecture architecture)
        {
            UnixStatLayoutResult result = UnixStatLayoutResolver.Resolve(
                isMacOS,
                isLinux,
                architecture);

            Assert.That(result.Success, Is.False);
        }

        private static FakeUnixNativeFileSystem CreateSecureFileSystem()
        {
            FakeUnixNativeFileSystem fileSystem = new(EffectiveUserId);
            fileSystem.SetMetadata(ParentPath, false, Metadata(UnixFileKind.Directory, 0, 0x3FF));
            fileSystem.SetMetadata(ParentPath, true, Metadata(UnixFileKind.Directory, 0, 0x3FF));
            fileSystem.SetMetadata(
                EndpointDirectoryPath,
                false,
                Metadata(UnixFileKind.Directory, EffectiveUserId, 0x1C0));
            return fileSystem;
        }

        private static UnixFileMetadata Metadata(UnixFileKind kind, uint ownerUserId, uint mode)
        {
            return UnixFileMetadata.Existing(kind, ownerUserId, mode);
        }

        private sealed class FakeUnixNativeFileSystem : IUnixNativeFileSystem
        {
            private readonly Dictionary<string, UnixFileMetadata> _noFollowMetadata = new();
            private readonly Dictionary<string, UnixFileMetadata> _followMetadata = new();

            public uint EffectiveUserId { get; }
            public List<uint> CreatedModes { get; } = new();
            public List<uint> ChangedModes { get; } = new();
            public UnixFileMetadata MetadataAfterCreate { get; set; }
            public UnixNativeOperationResult CreateResult { get; set; } = UnixNativeOperationResult.Successful();

            public FakeUnixNativeFileSystem(uint effectiveUserId)
            {
                EffectiveUserId = effectiveUserId;
                MetadataAfterCreate = UnixFileMetadata.Missing();
            }

            public uint GetEffectiveUserId()
            {
                return EffectiveUserId;
            }

            public UnixFileMetadata ReadMetadata(string path, bool followSymbolicLinks)
            {
                Dictionary<string, UnixFileMetadata> metadata =
                    followSymbolicLinks ? _followMetadata : _noFollowMetadata;
                if (metadata.ContainsKey(path))
                {
                    return metadata[path];
                }

                return UnixFileMetadata.Missing();
            }

            public UnixNativeOperationResult CreateDirectory(string path, uint mode)
            {
                CreatedModes.Add(mode);
                SetMetadata(path, false, MetadataAfterCreate);
                return CreateResult;
            }

            public UnixNativeOperationResult ChangeMode(string path, uint mode)
            {
                ChangedModes.Add(mode);
                if (_noFollowMetadata.ContainsKey(path))
                {
                    UnixFileMetadata current = _noFollowMetadata[path];
                    _noFollowMetadata[path] = UnixFileMetadata.Existing(
                        current.Kind,
                        current.OwnerUserId,
                        mode);
                }
                return UnixNativeOperationResult.Successful();
            }

            public void SetMetadata(
                string path,
                bool followSymbolicLinks,
                UnixFileMetadata metadata)
            {
                Dictionary<string, UnixFileMetadata> target =
                    followSymbolicLinks ? _followMetadata : _noFollowMetadata;
                target[path] = metadata;
            }
        }
    }
}
