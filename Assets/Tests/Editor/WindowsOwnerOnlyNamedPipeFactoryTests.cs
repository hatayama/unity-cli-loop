using System.IO.Pipes;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Infrastructure;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies the owner-only named pipe factory on the Windows editor.
    /// </summary>
    public class WindowsOwnerOnlyNamedPipeFactoryTests
    {
        [SetUp]
        public void SetUp()
        {
            if (UnityEngine.Application.platform != UnityEngine.RuntimePlatform.WindowsEditor)
            {
                Assert.Ignore("Windows named pipe factory tests only run on the Windows editor.");
            }
        }

        [Test]
        public void BuildCurrentUserOnlySddl_ReturnsProtectedDaclWithSingleUserAce()
        {
            // Tests that the SDDL grants FullControl to exactly one SID and marks the DACL
            // protected, because any extra ACE would widen access beyond the Editor owner.
            string sddl = WindowsOwnerOnlyNamedPipeFactory.BuildCurrentUserOnlySddl();

            StringAssert.StartsWith("D:P(A;;FA;;;S-1-5-", sddl);
            StringAssert.EndsWith(")", sddl);
            Assert.That(sddl.Split('(').Length - 1, Is.EqualTo(1), "SDDL must contain exactly one ACE");
        }

        [Test]
        public void CreateServer_ReturnsUsableServerStream()
        {
            // Tests that the native pipe creation and managed handle wrap succeed on this
            // runtime, because the Mono PipeSecurity overload silently ignores ACLs and the
            // factory is the only path that actually applies the descriptor.
            string sddl = WindowsOwnerOnlyNamedPipeFactory.BuildCurrentUserOnlySddl();
            string pipeName = "uloop-factory-test-" + System.Guid.NewGuid().ToString("N").Substring(0, 8);

            using NamedPipeServerStream server = WindowsOwnerOnlyNamedPipeFactory.CreateServer(pipeName, sddl);

            Assert.That(server, Is.Not.Null);
            Assert.That(server.SafePipeHandle.IsInvalid, Is.False);
        }

        [Test]
        public void CreateServer_AllowsMultipleConcurrentInstancesOfSamePipe()
        {
            // Tests that a second server instance of the same pipe name can be created while
            // the first is still open, because the accept loop creates a new instance per
            // client and the ACL must not break PIPE_UNLIMITED_INSTANCES.
            string sddl = WindowsOwnerOnlyNamedPipeFactory.BuildCurrentUserOnlySddl();
            string pipeName = "uloop-factory-test-" + System.Guid.NewGuid().ToString("N").Substring(0, 8);

            using NamedPipeServerStream first = WindowsOwnerOnlyNamedPipeFactory.CreateServer(pipeName, sddl);
            using NamedPipeServerStream second = WindowsOwnerOnlyNamedPipeFactory.CreateServer(pipeName, sddl);

            Assert.That(first.SafePipeHandle.IsInvalid, Is.False);
            Assert.That(second.SafePipeHandle.IsInvalid, Is.False);
        }
    }
}
