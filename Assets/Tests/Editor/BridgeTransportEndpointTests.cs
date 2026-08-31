using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Infrastructure;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies Bridge Transport Endpoint behavior.
    /// </summary>
    public class BridgeTransportEndpointTests
    {
        private const string SharedEndpointContractPath = "tests/contracts/endpoint_contract.json";

        [Test]
        public void CanonicalizeProjectRoot_WhenPathIsFilesystemRoot_ShouldPreserveRoot()
        {
            string filesystemRoot = Path.GetPathRoot(Directory.GetCurrentDirectory());

            // Tests that root path canonicalization keeps the filesystem root stable.
            string canonicalProjectRoot = ProjectRootCanonicalizer.Canonicalize(filesystemRoot);

            Assert.That(canonicalProjectRoot, Is.EqualTo(filesystemRoot));
        }

        [Test]
        public void CreateProjectIpc_WhenCanonicalRootsFromSharedContract_MatchExpectedEndpointPaths()
        {
            // Verifies Go and C# derive the same IPC endpoint names from already-canonicalized roots
            // listed in the shared endpoint contract (symlink resolution is out of scope).
            foreach (JObject contractCase in ReadEndpointContractCases())
            {
                string caseId = contractCase.Value<string>("id");
                string canonicalProjectRoot = contractCase.Value<string>("canonicalProjectRoot");
                if (!CanExerciseProjectRootOnCurrentPlatform(canonicalProjectRoot))
                {
                    continue;
                }

                string expectedPath = ExpectedEndpointPathForCurrentPlatform(contractCase);
                BridgeTransportEndpoint endpoint = BridgeTransportEndpoint.CreateProjectIpc(canonicalProjectRoot);
                Assert.That(endpoint.Path, Is.EqualTo(expectedPath), $"case {caseId} canonical root");

                JArray trimOnlyEquivalentRoots = contractCase.Value<JArray>("trimOnlyEquivalentRoots") ?? new JArray();
                foreach (JToken equivalentRootToken in trimOnlyEquivalentRoots)
                {
                    string equivalentRoot = equivalentRootToken.Value<string>();
                    BridgeTransportEndpoint trimmedEndpoint = BridgeTransportEndpoint.CreateProjectIpc(equivalentRoot);
                    Assert.That(
                        trimmedEndpoint.Path,
                        Is.EqualTo(expectedPath),
                        $"case {caseId} trim-only equivalent root {equivalentRoot}");
                }
            }
        }

        /// <summary>
        /// Verifies the contract permits a symlinked parent but never a symlinked endpoint directory.
        /// </summary>
        [Test]
        public void UnixSecurityPolicy_WhenRead_DefinesParentAndEndpointSymlinkBoundary()
        {
            JObject contract = ReadEndpointContract();
            JObject policy = contract.Value<JObject>("unixSecurityPolicy");

            Assert.That(policy, Is.Not.Null);
            Assert.That(policy.Value<string>("parentPath"), Is.EqualTo("/tmp"));
            Assert.That(policy.Value<bool>("parentMayBeSymbolicLink"), Is.True);
            Assert.That(policy.Value<uint>("resolvedParentOwnerUid"), Is.EqualTo(0));
            Assert.That(policy.Value<bool>("resolvedParentRequiresStickyBit"), Is.True);
            Assert.That(policy.Value<bool>("endpointDirectoryMayBeSymbolicLink"), Is.False);
            Assert.That(policy.Value<string>("endpointDirectoryMode"), Is.EqualTo("0700"));
            Assert.That(
                policy.Value<JArray>("endpointDirectoryRejectedSpecialModes")?.Values<string>(),
                Is.EqualTo(new[] { "04700", "02700" }));
            Assert.That(policy.Value<string>("socketMode"), Is.EqualTo("0600"));
        }

        private static IEnumerable<JObject> ReadEndpointContractCases()
        {
            JObject contract = ReadEndpointContract();
            JArray cases = contract.Value<JArray>("cases");
            Assert.That(cases, Is.Not.Null.And.Not.Empty);
            foreach (JToken caseToken in cases)
            {
                yield return (JObject)caseToken;
            }
        }

        private static JObject ReadEndpointContract()
        {
            string projectRoot = UnityCliLoopPathResolver.GetProjectRoot();
            string json = File.ReadAllText(Path.Combine(projectRoot, SharedEndpointContractPath));
            return JObject.Parse(json);
        }

        private static bool CanExerciseProjectRootOnCurrentPlatform(string projectRoot)
        {
            bool looksLikeWindowsPath = projectRoot.Length >= 2 && projectRoot[1] == ':';
            if (looksLikeWindowsPath)
            {
                return RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
            }

            return !RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        }

        private static string ExpectedEndpointPathForCurrentPlatform(JObject contractCase)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return contractCase.Value<string>("windowsPipePath");
            }

            string template = contractCase.Value<string>("unixSocketPathTemplate");
            uint effectiveUserId = new UnixNativeFileSystem().GetEffectiveUserId();
            return template.Replace("<UID>", effectiveUserId.ToString(CultureInfo.InvariantCulture));
        }
    }
}
