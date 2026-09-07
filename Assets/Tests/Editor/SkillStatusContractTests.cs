using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.Infrastructure;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies the Editor-side installed state against the skill status contract
    /// shared with the Go CLI, so the comparable-skill-file rule cannot drift between the two.
    /// </summary>
    [TestFixture]
    public class SkillStatusContractTests
    {
        private const string SharedContractPath = "tests/contracts/skill_status_contract.json";
        private const string InstalledExpectation = "installed";
        private const string OutdatedExpectation = "outdated";

        // Stands in for the whole case list when the contract yields none, so an unreadable or
        // empty contract fails loudly instead of reporting as "every case passed".
        private const string EmptyContractCaseId = "shared-contract-contains-no-case";

        private readonly List<string> _temporaryRoots = new();

        [TearDown]
        public void TearDown()
        {
            foreach (string temporaryRoot in _temporaryRoots)
            {
                if (Directory.Exists(temporaryRoot))
                {
                    Directory.Delete(temporaryRoot, true);
                }
            }

            _temporaryRoots.Clear();
        }

        /// <summary>
        /// Tests that the Editor-side installed state matches the shared skill status contract that the Go CLI also verifies.
        /// </summary>
        [TestCaseSource(nameof(SkillStatusContractCases))]
        public void GetInstalledStateForSkillSource_WhenCaseFromSharedContract_MatchesExpectedState(
            string caseId,
            JObject sourceFiles,
            JObject installedFiles,
            string expected)
        {
            if (string.Equals(caseId, EmptyContractCaseId, StringComparison.Ordinal))
            {
                Assert.Fail("shared skill status contract must contain at least one case");
                return;
            }

            string temporaryRoot = CreateTemporaryRoot();

            // CollectSourceSkillFiles walks the whole directory only when it is named "Skill",
            // so a differently named source directory would silently compare SKILL.md alone.
            string sourceDirectory = Path.Combine(temporaryRoot, "source", "Skill");
            WriteContractFiles(sourceDirectory, sourceFiles);
            SkillInstallLayout.SkillSourceInfo skill =
                SkillInstallLayout.GetSkillSourceInfoFromDirectory(sourceDirectory);

            string targetRoot = Path.Combine(temporaryRoot, ".claude");
            string installedDirectory = SkillInstallLayout.GetInstalledSkillDirectoryPathForLayout(
                targetRoot,
                skill.Name,
                true);
            WriteContractFiles(installedDirectory, installedFiles);

            SkillInstallState actualState = SkillInstallLayout.GetInstalledStateForSkillSource(
                targetRoot,
                skill,
                true);
            Assert.That(actualState, Is.EqualTo(ParseExpectedState(expected, caseId)), $"case {caseId}");
        }

        /// <summary>
        /// Expands every case of the shared skill status contract into its own test case, so one
        /// rejected case can never hide a missing check in another.
        /// </summary>
        private static IEnumerable<TestCaseData> SkillStatusContractCases()
        {
            JArray contractCases = ReadContract().Value<JArray>("cases");
            if (contractCases == null || contractCases.Count == 0)
            {
                yield return new TestCaseData(EmptyContractCaseId, null, null, null)
                    .SetName(EmptyContractCaseId);
                yield break;
            }

            foreach (JObject contractCase in contractCases)
            {
                string caseId = contractCase.Value<string>("id");
                yield return new TestCaseData(
                        caseId,
                        contractCase.Value<JObject>("source"),
                        contractCase.Value<JObject>("installed"),
                        contractCase.Value<string>("expected"))
                    .SetName(caseId);
            }
        }

        private static JObject ReadContract()
        {
            string contractPath = Path.Combine(
                UnityCliLoopPathResolver.GetProjectRoot(),
                SharedContractPath.Replace('/', Path.DirectorySeparatorChar));
            Assert.That(File.Exists(contractPath), Is.True, $"shared contract not found at {contractPath}");

            return JObject.Parse(File.ReadAllText(contractPath));
        }

        // Only installed and outdated are shared: the missing state has different names and
        // different conditions on each side, so it is deliberately out of the contract's scope.
        private static SkillInstallState ParseExpectedState(string expected, string caseId)
        {
            if (string.Equals(expected, InstalledExpectation, StringComparison.Ordinal))
            {
                return SkillInstallState.Installed;
            }

            if (string.Equals(expected, OutdatedExpectation, StringComparison.Ordinal))
            {
                return SkillInstallState.Outdated;
            }

            Assert.Fail($"case {caseId}: unsupported expected state '{expected}'");
            return SkillInstallState.Missing;
        }

        // Contract paths always use '/' so both sides read the same file; each side converts them
        // to its own separator before touching the filesystem.
        private static void WriteContractFiles(string root, JObject files)
        {
            Assert.That(files, Is.Not.Null, "contract case must declare its files");

            foreach (JProperty file in files.Properties())
            {
                string fullPath = Path.Combine(root, file.Name.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
                File.WriteAllText(fullPath, file.Value.Value<string>());
            }
        }

        private string CreateTemporaryRoot()
        {
            string temporaryRoot = Path.Combine(
                Path.GetTempPath(),
                nameof(SkillStatusContractTests),
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temporaryRoot);
            _temporaryRoots.Add(temporaryRoot);
            return temporaryRoot;
        }
    }
}
