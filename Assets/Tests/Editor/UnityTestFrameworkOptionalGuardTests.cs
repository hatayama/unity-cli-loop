using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies Unity Test Framework remains optional for package consumers.
    /// </summary>
    public class UnityTestFrameworkOptionalGuardTests
    {
        private const string PackageManifestPath = "Packages/src/package.json";
        private const string PackageLockPath = "Packages/packages-lock.json";
        private const string UnityEditorTestRunnerGuidReference = "GUID:0acc523941302664db1f4e527237feb3";
        private const string RunTestsAsmdefPath =
            "Packages/src/Editor/FirstPartyTools/RunTests/UnityCLILoop.FirstPartyTools.RunTests.Editor.asmdef";
        private static readonly string[] UnityTestRunnerApiFiles =
        {
            "Packages/src/Editor/FirstPartyTools/RunTests/TestRunner/PlayModeTestExecuter.cs",
            "Packages/src/Editor/FirstPartyTools/RunTests/TestRunner/NUnitXmlResultExporter.cs",
            "Packages/src/Editor/FirstPartyTools/RunTests/TestRunner/SerializableTestResult.cs"
        };

        [Test]
        public void PackageManifest_WhenScanned_DoesNotRequireUnityTestFramework()
        {
            // Verifies that installing the package does not implicitly install Unity Test Framework.
            JObject manifest = ReadJson(PackageManifestPath);

            Assert.That(manifest["dependencies"]?[UnityCliLoopConstants.PACKAGE_NAME_TEST_FRAMEWORK], Is.Null);
        }

        [Test]
        public void EmbeddedPackageLock_WhenScanned_DoesNotRequireUnityTestFramework()
        {
            // Verifies that the embedded package lock mirrors the optional dependency contract.
            JObject packagesLock = ReadJson(PackageLockPath);
            JToken embeddedPackageDependencies = packagesLock["dependencies"]?["io.github.hatayama.uloopmcp"]?["dependencies"];

            Assert.That(embeddedPackageDependencies?[UnityCliLoopConstants.PACKAGE_NAME_TEST_FRAMEWORK], Is.Null);
        }

        [Test]
        public void RunTestsAssemblyDefinition_WhenScanned_DefinesOptionalTestFrameworkSymbol()
        {
            // Verifies that RunTests can compile different code paths based on package availability.
            JObject asmdef = ReadJson(RunTestsAsmdefPath);
            bool hasVersionDefine = asmdef["versionDefines"]
                ?.Any(DefinesUnityTestFrameworkSymbol) == true;

            Assert.That(hasVersionDefine, Is.True);
        }

        [Test]
        public void RunTestsAssemblyDefinition_WhenScanned_DoesNotReferenceTestRunnerAssemblyByGuid()
        {
            // Verifies that the optional dependency is not reintroduced through asmdef references.
            string content = ReadText(RunTestsAsmdefPath);

            Assert.That(content, Does.Not.Contain(UnityEditorTestRunnerGuidReference));
        }

        [Test]
        public void UnityTestRunnerApiFiles_WhenScanned_GuardApiReferencesWithOptionalSymbol()
        {
            // Verifies that consumer projects without Unity Test Framework compile the RunTests assembly.
            foreach (string relativePath in UnityTestRunnerApiFiles)
            {
                string content = ReadText(relativePath);
                int guardIndex = content.IndexOf("#if " + UnityCliLoopConstants.SCRIPTING_DEFINE_HAS_TEST_FRAMEWORK);
                int usingIndex = content.IndexOf("using UnityEditor.TestTools.TestRunner.Api;");

                Assert.That(usingIndex, Is.GreaterThanOrEqualTo(0), relativePath);
                Assert.That(guardIndex, Is.GreaterThanOrEqualTo(0), relativePath);
                Assert.That(guardIndex, Is.LessThan(usingIndex), relativePath);
            }
        }

        private static bool DefinesUnityTestFrameworkSymbol(JToken versionDefine)
        {
            return versionDefine["name"]?.Value<string>() == UnityCliLoopConstants.PACKAGE_NAME_TEST_FRAMEWORK
                && versionDefine["define"]?.Value<string>() == UnityCliLoopConstants.SCRIPTING_DEFINE_HAS_TEST_FRAMEWORK;
        }

        private static JObject ReadJson(string relativePath)
        {
            return JObject.Parse(ReadText(relativePath));
        }

        private static string ReadText(string relativePath)
        {
            string projectRoot = Directory.GetCurrentDirectory();
            string absolutePath = Path.Combine(projectRoot, relativePath);
            return File.ReadAllText(absolutePath);
        }
    }
}
