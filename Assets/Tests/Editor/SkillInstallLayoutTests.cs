using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.Infrastructure;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies skill layout discovery behavior.
    /// </summary>
    [TestFixture]
    public class SkillInstallLayoutTests
    {
        private string _projectRoot;
        private string[] _temporaryRoots;

        [SetUp]
        public void SetUp()
        {
            _projectRoot = UnityCliLoopPathResolver.GetProjectRoot();
            _temporaryRoots = new string[0];
        }

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
        }

        // Tests that skill installation detection accepts managed legacy and namespaced skills only.
        [Test]
        public void AreSkillsInstalled_ReturnsTrueForManagedLegacyAndNamespacedSkillsOnly()
        {
            string temporaryRoot = CreateTemporaryProjectRoot();
            CreateFakeSourceSkill(temporaryRoot, "uloop-compile", "CompileTool", "reference.md", "reference");

            string manualTargetRoot = Path.Combine(temporaryRoot, ".claude");
            WriteSkillFile(
                Path.Combine(manualTargetRoot, SkillInstallLayout.SkillsDirName, "find-orphaned-meta"),
                "---\nname: find-orphaned-meta\n---\n");
            SkillInstallationDetector detector = new();
            Assert.IsFalse(detector.AreSkillsInstalledInAnyLayout(temporaryRoot, ".claude"),
                "Manual local skills should not be treated as installed uLoop skills");

            string legacyTargetRoot = Path.Combine(temporaryRoot, ".codex");
            WriteSkillFile(
                Path.Combine(legacyTargetRoot, SkillInstallLayout.SkillsDirName, "acme-third-party"),
                "---\nname: acme-third-party\ntoolName: acme-third-party\n---\n");
            Assert.IsTrue(detector.AreSkillsInstalledInAnyLayout(temporaryRoot, ".codex"),
                "Legacy third-party managed skills should be detected");

            string managedTargetRoot = Path.Combine(temporaryRoot, ".agents");
            WriteSkillFile(Path.Combine(
                managedTargetRoot,
                SkillInstallLayout.SkillsDirName,
                SkillInstallLayout.ManagedSkillsDirName,
                "uloop-compile"));
            Assert.IsTrue(detector.AreSkillsInstalledInAnyLayout(temporaryRoot, ".agents"),
                "Namespaced managed skills should be detected");
        }

        // Tests that layout-specific detection only matches the selected layout.
        [Test]
        public void AreSkillsInstalled_WhenLayoutSpecified_MatchesOnlySelectedLayout()
        {
            string temporaryRoot = CreateTemporaryProjectRoot();
            CreateFakeSourceSkill(temporaryRoot, "uloop-compile", "CompileTool", "reference.md", "reference");
            SkillInstallationDetector detector = new();

            string flatTargetRoot = Path.Combine(temporaryRoot, ".claude");
            WriteSkillFile(Path.Combine(flatTargetRoot, SkillInstallLayout.SkillsDirName, "uloop-compile"));
            Assert.IsTrue(detector.AreSkillsInstalledForLayout(temporaryRoot, ".claude", false));
            Assert.IsFalse(detector.AreSkillsInstalledForLayout(temporaryRoot, ".claude", true));

            string groupedTargetRoot = Path.Combine(temporaryRoot, ".codex");
            WriteSkillFile(Path.Combine(
                groupedTargetRoot,
                SkillInstallLayout.SkillsDirName,
                SkillInstallLayout.ManagedSkillsDirName,
                "uloop-compile"));
            Assert.IsTrue(detector.AreSkillsInstalledForLayout(temporaryRoot, ".codex", true));
            Assert.IsFalse(detector.AreSkillsInstalledForLayout(temporaryRoot, ".codex", false));
        }

        // Tests that an empty legacy managed directory still counts for flat layout migration state.
        [Test]
        public void AreSkillsInstalled_WhenLegacyManagedDirectoryIsEmpty_StillDetectsFlatLayout()
        {
            string temporaryRoot = CreateTemporaryProjectRoot();
            string targetRoot = Path.Combine(temporaryRoot, ".codex");
            Directory.CreateDirectory(Path.Combine(targetRoot, SkillInstallLayout.SkillsDirName, "uloop-compile"));
            SkillInstallationDetector detector = new();

            Assert.IsTrue(detector.AreSkillsInstalledForLayout(temporaryRoot, ".codex", false));
            Assert.IsFalse(detector.AreSkillsInstalledForLayout(temporaryRoot, ".codex", true));
        }

        // Tests that Unity-side discovery includes CLI-only skills from the packaged core CLI.
        [Test]
        public void GetSkillSourceInfos_WhenProjectIsCurrentRoot_IncludesCliOnlyCoreSkills()
        {
            SkillInstallLayout.SkillSourceInfo[] skillSources = SkillInstallLayout.GetSkillSourceInfos(_projectRoot)
                .ToArray();

            Assert.That(skillSources.Select(skill => skill.Name), Does.Contain("uloop-launch"));
        }

        // Tests that differently cased paths follow each editor platform's path identity policy.
        [TestCase(UnityEngine.RuntimePlatform.WindowsEditor, true)]
        [TestCase(UnityEngine.RuntimePlatform.OSXEditor, true)]
        [TestCase(UnityEngine.RuntimePlatform.LinuxEditor, false)]
        public void HaveSamePathIdentityAtPlatform_WhenCasingDiffers_UsesPlatformPolicy(
            UnityEngine.RuntimePlatform platform,
            bool expected)
        {
            string lowerCasePath = Path.Combine(_projectRoot, "case-sensitive-root");
            string upperCasePath = Path.Combine(_projectRoot, "CASE-SENSITIVE-ROOT");

            bool actual = SkillSourceRootEnumerator.HaveSamePathIdentityAtPlatform(
                lowerCasePath,
                upperCasePath,
                platform);

            Assert.That(actual, Is.EqualTo(expected));
        }

        // Tests that an optional trailing directory separator does not change path identity.
        [TestCase(UnityEngine.RuntimePlatform.WindowsEditor)]
        [TestCase(UnityEngine.RuntimePlatform.OSXEditor)]
        [TestCase(UnityEngine.RuntimePlatform.LinuxEditor)]
        public void HaveSamePathIdentityAtPlatform_WhenTrailingSeparatorDiffers_ReturnsTrue(
            UnityEngine.RuntimePlatform platform)
        {
            string path = Path.Combine(_projectRoot, "skill-source-root");
            string pathWithTrailingSeparator = path + Path.DirectorySeparatorChar;

            bool actual = SkillSourceRootEnumerator.HaveSamePathIdentityAtPlatform(
                path,
                pathWithTrailingSeparator,
                platform);

            Assert.That(actual, Is.True);
        }

        // Tests that Tool Settings can read source skill frontmatter descriptions by tool name.
        [Test]
        public void GetToolDescriptionsByToolName_WhenSkillHasDescription_MapsDescriptionToToolName()
        {
            IReadOnlyDictionary<string, string> descriptions = SkillInstallLayout.GetToolDescriptionsByToolName(_projectRoot);

            Assert.That(descriptions["compile"], Is.EqualTo("Compile the Unity project and report errors/warnings. Use after C# edits."));
            Assert.That(descriptions[UnityCliLoopConstants.SETTINGS_TOOL_NAME_PAUSE_POINT], Does.StartWith("Pauses Unity playback"));
        }

        // Tests that duplicate skill names use the earlier source root across each precedence boundary.
        [Test]
        public void GetSkillSourceInfos_WhenNamesOverlap_PrefersEarlierSourceRoot()
        {
            string temporaryRoot = CreateTemporaryProjectRoot();
            string manifestPackageRoot = CreateTemporaryProjectRoot();
            string assetsRoot = Path.Combine(temporaryRoot, "Assets");
            string directPackageRoot = Path.Combine(temporaryRoot, "Packages", "com.example.direct");
            string cachePackageRoot = Path.Combine(
                temporaryRoot,
                "Library",
                "PackageCache",
                "com.example.cached@1.0.0");

            WriteSourceSkill(assetsRoot, "uloop-assets-priority", "assets-winner", "AssetsWinner");
            WriteSourceSkill(directPackageRoot, "uloop-assets-priority", "direct-loser", "DirectLoser");
            WriteSourceSkill(directPackageRoot, "uloop-package-priority", "direct-winner", "DirectWinner");
            WriteSourceSkill(manifestPackageRoot, "uloop-package-priority", "manifest-loser", "ManifestLoser");
            WriteSourceSkill(manifestPackageRoot, "uloop-manifest-priority", "manifest-winner", "ManifestWinner");
            WriteSourceSkill(cachePackageRoot, "uloop-manifest-priority", "cache-loser", "CacheLoser");

            string manifestPath = Path.Combine(temporaryRoot, "Packages", "manifest.json");
            Directory.CreateDirectory(Path.GetDirectoryName(manifestPath));
            string portableManifestPackageRoot = manifestPackageRoot.Replace(Path.DirectorySeparatorChar, '/');
            File.WriteAllText(
                manifestPath,
                $"{{\"dependencies\":{{\"com.example.manifest\":\"file:{portableManifestPackageRoot}\"," +
                "\"com.example.cached\":\"1.0.0\"}}}");

            Dictionary<string, SkillInstallLayout.SkillSourceInfo> skillSources = SkillInstallLayout
                .GetSkillSourceInfos(temporaryRoot)
                .ToDictionary(skill => skill.Name, StringComparer.Ordinal);

            Assert.That(skillSources["uloop-assets-priority"].ToolName, Is.EqualTo("assets-winner"));
            Assert.That(skillSources["uloop-package-priority"].ToolName, Is.EqualTo("direct-winner"));
            Assert.That(skillSources["uloop-manifest-priority"].ToolName, Is.EqualTo("manifest-winner"));
        }

        // Tests that local manifest dependencies do not also discover stale PackageCache-only skills.
        [TestCase("file:")]
        [TestCase("path:")]
        public void GetSkillSourceInfos_WhenLocalDependencyHasStaleCache_ExcludesCacheOnlySkill(
            string dependencyPrefix)
        {
            string temporaryRoot = CreateTemporaryProjectRoot();
            string localPackageRoot = CreateTemporaryProjectRoot();
            string cachePackageRoot = Path.Combine(
                temporaryRoot,
                "Library",
                "PackageCache",
                "com.example.local@1.0.0");
            WriteSourceSkill(localPackageRoot, "uloop-local-skill", "local-tool", "LocalTool");
            WriteSourceSkill(cachePackageRoot, "uloop-stale-cache-skill", "stale-tool", "StaleTool");

            string manifestPath = Path.Combine(temporaryRoot, "Packages", "manifest.json");
            Directory.CreateDirectory(Path.GetDirectoryName(manifestPath));
            string portableLocalPackageRoot = localPackageRoot.Replace(Path.DirectorySeparatorChar, '/');
            File.WriteAllText(
                manifestPath,
                $"{{\"dependencies\":{{\"com.example.local\":" +
                $"\"{dependencyPrefix}{portableLocalPackageRoot}\"}}}}");

            string[] skillNames = SkillInstallLayout.GetSkillSourceInfos(temporaryRoot)
                .Select(skill => skill.Name)
                .ToArray();

            Assert.That(skillNames, Does.Contain("uloop-local-skill"));
            Assert.That(skillNames, Does.Not.Contain("uloop-stale-cache-skill"));
        }

        // Tests that skill discovery follows bundled tools after they move into the first-party plugin assembly.
        [Test]
        public void GetSkillSourceInfos_WhenFirstPartyToolIsUnderFirstPartyTools_IncludesToolSkill()
        {
            SkillInstallLayout.SkillSourceInfo[] skillSources = SkillInstallLayout.GetSkillSourceInfos(_projectRoot)
                .ToArray();

            SkillInstallLayout.SkillSourceInfo controlPlayModeSkill = skillSources
                .Single(skill => skill.Name == "uloop-control-play-mode");

            Assert.That(controlPlayModeSkill.ToolName, Is.EqualTo("control-play-mode"));
            Assert.That(controlPlayModeSkill.SkillFiles.Keys, Does.Contain(SkillInstallLayout.SkillFileName));

            SkillInstallLayout.SkillSourceInfo getLogsSkill = skillSources
                .Single(skill => skill.Name == "uloop-get-logs");

            Assert.That(getLogsSkill.ToolName, Is.EqualTo("get-logs"));
            Assert.That(getLogsSkill.SkillFiles.Keys, Does.Contain(SkillInstallLayout.SkillFileName));

            SkillInstallLayout.SkillSourceInfo compileSkill = skillSources
                .Single(skill => skill.Name == "uloop-compile");

            Assert.That(compileSkill.ToolName, Is.EqualTo("compile"));
            Assert.That(compileSkill.SkillFiles.Keys, Does.Contain(SkillInstallLayout.SkillFileName));

            SkillInstallLayout.SkillSourceInfo executeDynamicCodeSkill = skillSources
                .Single(skill => skill.Name == "uloop-execute-dynamic-code");

            Assert.That(executeDynamicCodeSkill.ToolName, Is.EqualTo("execute-dynamic-code"));
            Assert.That(executeDynamicCodeSkill.SkillFiles.Keys, Does.Contain(SkillInstallLayout.SkillFileName));

            SkillInstallLayout.SkillSourceInfo clearConsoleSkill = skillSources
                .Single(skill => skill.Name == "uloop-clear-console");

            Assert.That(clearConsoleSkill.ToolName, Is.EqualTo("clear-console"));
            Assert.That(clearConsoleSkill.SkillFiles.Keys, Does.Contain(SkillInstallLayout.SkillFileName));

            SkillInstallLayout.SkillSourceInfo getHierarchySkill = skillSources
                .Single(skill => skill.Name == "uloop-get-hierarchy");

            Assert.That(getHierarchySkill.ToolName, Is.EqualTo("get-hierarchy"));
            Assert.That(getHierarchySkill.SkillFiles.Keys, Does.Contain(SkillInstallLayout.SkillFileName));

            SkillInstallLayout.SkillSourceInfo runTestsSkill = skillSources
                .Single(skill => skill.Name == "uloop-run-tests");

            Assert.That(runTestsSkill.ToolName, Is.EqualTo("run-tests"));
            Assert.That(runTestsSkill.SkillFiles.Keys, Does.Contain(SkillInstallLayout.SkillFileName));

            SkillInstallLayout.SkillSourceInfo findGameObjectsSkill = skillSources
                .Single(skill => skill.Name == "uloop-find-game-objects");

            Assert.That(findGameObjectsSkill.ToolName, Is.EqualTo("find-game-objects"));
            Assert.That(findGameObjectsSkill.SkillFiles.Keys, Does.Contain(SkillInstallLayout.SkillFileName));

            SkillInstallLayout.SkillSourceInfo screenshotSkill = skillSources
                .Single(skill => skill.Name == "uloop-screenshot");

            Assert.That(screenshotSkill.ToolName, Is.EqualTo("screenshot"));
            Assert.That(screenshotSkill.SkillFiles.Keys, Does.Contain(SkillInstallLayout.SkillFileName));

            SkillInstallLayout.SkillSourceInfo replayInputSkill = skillSources
                .Single(skill => skill.Name == "uloop-replay-input");

            Assert.That(replayInputSkill.ToolName, Is.EqualTo("replay-input"));
            Assert.That(replayInputSkill.SkillFiles.Keys, Does.Contain(SkillInstallLayout.SkillFileName));

            SkillInstallLayout.SkillSourceInfo simulateKeyboardSkill = skillSources
                .Single(skill => skill.Name == "uloop-simulate-keyboard");

            Assert.That(simulateKeyboardSkill.ToolName, Is.EqualTo("simulate-keyboard"));
            Assert.That(simulateKeyboardSkill.SkillFiles.Keys, Does.Contain(SkillInstallLayout.SkillFileName));

            SkillInstallLayout.SkillSourceInfo simulateMouseInputSkill = skillSources
                .Single(skill => skill.Name == "uloop-simulate-mouse-input");

            Assert.That(simulateMouseInputSkill.ToolName, Is.EqualTo("simulate-mouse-input"));
            Assert.That(simulateMouseInputSkill.SkillFiles.Keys, Does.Contain(SkillInstallLayout.SkillFileName));

            SkillInstallLayout.SkillSourceInfo simulateMouseUiSkill = skillSources
                .Single(skill => skill.Name == "uloop-simulate-mouse-ui");

            Assert.That(simulateMouseUiSkill.ToolName, Is.EqualTo("simulate-mouse-ui"));
            Assert.That(simulateMouseUiSkill.SkillFiles.Keys, Does.Contain(SkillInstallLayout.SkillFileName));
        }

        // Tests that internal skill metadata maps back to the hidden tool name only.
        [Test]
        public void GetInternalSkillToolNames_WhenInternalSkillUsesSkillName_ReturnsToolName()
        {
            string temporaryRoot = CreateTemporaryProjectRoot();
            CreateFakeSourceSkill(
                temporaryRoot,
                "uloop-public-skill",
                "PublicTool",
                "reference.md",
                "reference");
            CreateFakeSourceSkill(
                temporaryRoot,
                "uloop-internal-skill",
                "InternalTool",
                "reference.md",
                "internal-reference",
                isInternal: true);

            HashSet<string> internalToolNames = SkillInstallLayout.GetInternalSkillToolNames(temporaryRoot);

            Assert.That(internalToolNames, Does.Contain("internal-skill"));
            Assert.That(internalToolNames, Does.Not.Contain("public-skill"));
        }

        // Tests that user-facing tool catalogs omit tools backed by internal skills.
        [Test]
        public void GetToolSettingsCatalogForProjectRoot_WhenSkillIsInternal_HidesToolFromUserFacingLists()
        {
            string temporaryRoot = CreateTemporaryProjectRoot();
            CreateFakeSourceSkill(
                temporaryRoot,
                "uloop-internal-tool",
                "InternalTool",
                "reference.md",
                "internal-reference",
                isInternal: true);

            UnityCliLoopToolRegistry registry = new UnityCliLoopToolRegistry(
                new ToolSettingsRepository(),
                new SkillInstallLayoutInternalToolNameProvider(),
                toolDiscovery: null);
            registry.RegisterTool(new FakeUnityTool("internal-tool"));
            registry.RegisterTool(new FakeUnityTool("public-tool"));

            string[] catalogNames = registry.GetToolSettingsCatalogForProjectRoot(temporaryRoot)
                .Select(tool => tool.Name)
                .ToArray();
            string[] registeredToolNames = registry.GetRegisteredToolsForProjectRoot(temporaryRoot)
                .Select(tool => tool.Name)
                .ToArray();

            Assert.That(catalogNames, Does.Not.Contain("internal-tool"));
            Assert.That(catalogNames, Does.Contain("public-tool"));
            Assert.That(registeredToolNames, Does.Not.Contain("internal-tool"));
            Assert.That(registeredToolNames, Does.Contain("public-tool"));
        }

        // Tests that skill setup file scans share the same generated-file exclusion behavior.
        [Test]
        public void IsExcludedSkillFile_WhenGeneratedSkillFileNameIsPassed_ReturnsTrue()
        {
            Assert.That(SkillSetupFileExclusion.IsExcludedSkillFile("reference.md.meta"), Is.True);
            Assert.That(SkillSetupFileExclusion.IsExcludedSkillFile(".DS_Store"), Is.True);
            Assert.That(SkillSetupFileExclusion.IsExcludedSkillFile(".gitkeep"), Is.True);
            Assert.That(SkillSetupFileExclusion.IsExcludedSkillFile("SKILL.md"), Is.False);
        }

        private string CreateTemporaryProjectRoot()
        {
            string temporaryRoot = Path.Combine(
                Path.GetTempPath(),
                "SkillInstallLayoutTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temporaryRoot);
            _temporaryRoots = _temporaryRoots.Append(temporaryRoot).ToArray();
            return temporaryRoot;
        }

        private static void WriteSkillFile(string skillDir, string content = "---\nname: uloop-compile\n---\n")
        {
            Directory.CreateDirectory(skillDir);
            File.WriteAllText(Path.Combine(skillDir, SkillInstallLayout.SkillFileName), content);
        }

        private static void CreateFakeSourceSkill(
            string projectRoot,
            string skillName,
            string toolDirectoryName,
            string additionalFileRelativePath,
            string additionalFileContent,
            bool isInternal = false)
        {
            string skillDir = Path.Combine(
                projectRoot,
                "Packages",
                "com.example.fake",
                "Editor",
                toolDirectoryName,
                "Skill");
            Directory.CreateDirectory(skillDir);
            string internalLine = isInternal ? "internal: true\n" : string.Empty;
            File.WriteAllText(
                Path.Combine(skillDir, SkillInstallLayout.SkillFileName),
                $"---\nname: {skillName}\n{internalLine}---\n");
            File.WriteAllText(Path.Combine(skillDir, additionalFileRelativePath), additionalFileContent);
        }

        private static void WriteSourceSkill(
            string sourceRoot,
            string skillName,
            string toolName,
            string toolDirectoryName)
        {
            string skillDirectory = Path.Combine(sourceRoot, "Editor", toolDirectoryName, "Skill");
            Directory.CreateDirectory(skillDirectory);
            File.WriteAllText(
                Path.Combine(skillDirectory, SkillInstallLayout.SkillFileName),
                $"---\nname: {skillName}\ntoolName: {toolName}\n---\n");
        }

        /// <summary>
        /// Test support type used by editor fixtures.
        /// </summary>
        private sealed class FakeUnityTool : IUnityCliLoopTool
        {
            public string ToolName { get; }

            public ToolParameterSchema ParameterSchema { get; } = new();

            public FakeUnityTool(string toolName)
            {
                ToolName = toolName;
            }

            public Task<UnityCliLoopToolResponse> ExecuteAsync(JToken paramsToken, CancellationToken ct)
            {
                return Task.FromResult<UnityCliLoopToolResponse>(new FakeToolResponse());
            }
        }

        /// <summary>
        /// Test support type used by editor fixtures.
        /// </summary>
        private sealed class FakeToolResponse : UnityCliLoopToolResponse
        {
        }
    }
}
