using System;
using System.Collections.Generic;
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

        private static IEnumerable<JObject> ReadEndpointContractCases()
        {
            string projectRoot = UnityCliLoopPathResolver.GetProjectRoot();
            string json = File.ReadAllText(Path.Combine(projectRoot, SharedEndpointContractPath));
            JObject contract = JObject.Parse(json);
            JArray cases = contract.Value<JArray>("cases");
            Assert.That(cases, Is.Not.Null.And.Not.Empty);
            foreach (JToken caseToken in cases)
            {
                yield return (JObject)caseToken;
            }
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

            return contractCase.Value<string>("unixSocketPath");
        }
    }
}
