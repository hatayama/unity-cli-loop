using System.IO;
using NUnit.Framework;
using UnityEngine.TestTools;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.DynamicCodeToolTests
{
    /// <summary>
    /// Test fixture that verifies External Compiler Path Resolver behavior.
    /// </summary>
    [TestFixture]
    public class ExternalCompilerPathResolverTests
    {
        private string _tempDirectoryPath;

        [SetUp]
        public void SetUp()
        {
            _tempDirectoryPath = Path.Combine(Path.GetTempPath(), $"ExternalCompilerPathResolverTests_{System.Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDirectoryPath);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_tempDirectoryPath))
            {
                Directory.Delete(_tempDirectoryPath, true);
            }
        }

        [Test]
        public void ResolveNetCoreRuntimeSharedDirectoryPath_WhenMultipleRuntimeVersionsExist_ShouldChooseHighestVersion()
        {
            string runtimeRootPath = CreateDirectory("Microsoft.NETCore.App");
            string olderRuntimeDirectoryPath = CreateDirectory(Path.Combine("Microsoft.NETCore.App", "8.0.0"));
            string latestRuntimeDirectoryPath = CreateDirectory(Path.Combine("Microsoft.NETCore.App", "8.0.14"));
            CreateDirectory(Path.Combine("Microsoft.NETCore.App", "7.0.20"));

            string resolvedDirectoryPath = ExternalCompilerPathResolver.ResolveNetCoreRuntimeSharedDirectoryPath(runtimeRootPath);

            Assert.That(resolvedDirectoryPath, Is.EqualTo(latestRuntimeDirectoryPath));
            Assert.That(resolvedDirectoryPath, Is.Not.EqualTo(olderRuntimeDirectoryPath));
        }

        [Test]
        public void ResolveNetCoreRuntimeSharedDirectoryPath_WhenVersionAndNonVersionDirectoriesExist_ShouldPreferHighestVersion()
        {
            string runtimeRootPath = CreateDirectory("Microsoft.NETCore.App");
            CreateDirectory(Path.Combine("Microsoft.NETCore.App", "current"));
            string latestRuntimeDirectoryPath = CreateDirectory(Path.Combine("Microsoft.NETCore.App", "9.0.1"));

            string resolvedDirectoryPath = ExternalCompilerPathResolver.ResolveNetCoreRuntimeSharedDirectoryPath(runtimeRootPath);

            Assert.That(resolvedDirectoryPath, Is.EqualTo(latestRuntimeDirectoryPath));
        }

        [Test]
        public void ResolveNetCoreRuntimeSharedDirectoryPath_WhenOnlyNonVersionDirectoriesExist_ShouldChooseDeterministicDirectory()
        {
            string runtimeRootPath = CreateDirectory("Microsoft.NETCore.App");
            CreateDirectory(Path.Combine("Microsoft.NETCore.App", "alpha"));
            string expectedDirectoryPath = CreateDirectory(Path.Combine("Microsoft.NETCore.App", "release"));

            string resolvedDirectoryPath = ExternalCompilerPathResolver.ResolveNetCoreRuntimeSharedDirectoryPath(runtimeRootPath);

            Assert.That(resolvedDirectoryPath, Is.EqualTo(expectedDirectoryPath));
        }

        [Test]
        public void ResolveScriptingRootPath_WhenLegacyLayoutExists_ShouldReturnContentsPath()
        {
            string contentsPath = CreateDirectory("Contents");
            CreateDirectory(Path.Combine("Contents", "NetCoreRuntime"));
            CreateDirectory(Path.Combine("Contents", "DotNetSdkRoslyn"));
            CreateFile(Path.Combine("Contents", "DotNetSdkRoslyn", "csc.dll"));

            string resolvedScriptingRootPath = ExternalCompilerPathResolver.ResolveScriptingRootPath(contentsPath);

            Assert.That(resolvedScriptingRootPath, Is.EqualTo(contentsPath));
        }

        [Test]
        public void ResolveCompilerLayoutKind_WhenContentsRootLegacyRoslynLayoutExists_ShouldReturnContentsRootDotNetSdkRoslyn()
        {
            // Verifies Unity 2022-style compiler roots are classified as legacy contents-root Roslyn.
            string contentsPath = CreateDirectory("Contents");
            string compilerDirectoryPath = CreateDirectory(Path.Combine("Contents", "DotNetSdkRoslyn"));

            ExternalCompilerLayoutKind layoutKind = ExternalCompilerPathResolver.ResolveCompilerLayoutKind(
                contentsPath,
                contentsPath,
                compilerDirectoryPath);

            Assert.That(layoutKind, Is.EqualTo(ExternalCompilerLayoutKind.ContentsRootDotNetSdkRoslyn));
        }

        [Test]
        public void ResolveCompilerLayoutKind_WhenResourcesScriptingRoslynLayoutExists_ShouldReturnResourcesScripting()
        {
            // Verifies Unity 6-style Resources/Scripting compiler roots stay on the current shared-worker path.
            string contentsPath = CreateDirectory("Contents");
            string scriptingRootPath = CreateDirectory(Path.Combine("Contents", "Resources", "Scripting"));
            string compilerDirectoryPath = CreateDirectory(Path.Combine("Contents", "Resources", "Scripting", "DotNetSdkRoslyn"));

            ExternalCompilerLayoutKind layoutKind = ExternalCompilerPathResolver.ResolveCompilerLayoutKind(
                contentsPath,
                scriptingRootPath,
                compilerDirectoryPath);

            Assert.That(layoutKind, Is.EqualTo(ExternalCompilerLayoutKind.ResourcesScripting));
        }

        [Test]
        public void ResolveScriptingRootPath_WhenResourcesScriptingLayoutExists_ShouldReturnResourcesScriptingPath()
        {
            // Verifies Unity's Resources/Scripting compiler layout is preferred when present.
            string contentsPath = CreateDirectory("Contents");
            string expectedScriptingRootPath = CreateDirectory(Path.Combine("Contents", "Resources", "Scripting"));
            CreateDirectory(Path.Combine("Contents", "Resources", "Scripting", "NetCoreRuntime"));
            CreateDirectory(Path.Combine("Contents", "Resources", "Scripting", "DotNetSdkRoslyn"));
            CreateFile(Path.Combine("Contents", "Resources", "Scripting", "DotNetSdkRoslyn", "csc.dll"));

            string resolvedScriptingRootPath = ExternalCompilerPathResolver.ResolveScriptingRootPath(contentsPath);

            Assert.That(resolvedScriptingRootPath, Is.EqualTo(expectedScriptingRootPath));
        }

        [Test]
        public void ResolveScriptingRootPath_WhenResourcesScriptingDotNetSdkLayoutExists_ShouldReturnResourcesScriptingPath()
        {
            // Verifies Unity 6.5 DotNetSdk compiler layouts are accepted under Resources/Scripting.
            string contentsPath = CreateDirectory("Contents");
            string expectedScriptingRootPath = CreateDirectory(Path.Combine("Contents", "Resources", "Scripting"));
            CreateDirectory(Path.Combine("Contents", "Resources", "Scripting", "NetCoreRuntime"));
            CreateDirectory(Path.Combine("Contents", "Resources", "Scripting", "DotNetSdk", "sdk", "8.0.318", "Roslyn", "bincore"));

            string resolvedScriptingRootPath = ExternalCompilerPathResolver.ResolveScriptingRootPath(contentsPath);

            Assert.That(resolvedScriptingRootPath, Is.EqualTo(expectedScriptingRootPath));
        }

        [Test]
        public void ResolveScriptingRootPath_WhenBothLayoutsExist_ShouldPreferResourcesScriptingLayout()
        {
            // Verifies the current Resources/Scripting layout wins over the legacy contents-root layout.
            string contentsPath = CreateDirectory("Contents");
            CreateDirectory(Path.Combine("Contents", "NetCoreRuntime"));
            CreateDirectory(Path.Combine("Contents", "DotNetSdkRoslyn"));
            CreateFile(Path.Combine("Contents", "DotNetSdkRoslyn", "csc.dll"));
            string expectedScriptingRootPath = CreateDirectory(Path.Combine("Contents", "Resources", "Scripting"));
            CreateDirectory(Path.Combine("Contents", "Resources", "Scripting", "NetCoreRuntime"));
            CreateDirectory(Path.Combine("Contents", "Resources", "Scripting", "DotNetSdkRoslyn"));
            CreateFile(Path.Combine("Contents", "Resources", "Scripting", "DotNetSdkRoslyn", "csc.dll"));

            string resolvedScriptingRootPath = ExternalCompilerPathResolver.ResolveScriptingRootPath(contentsPath);

            Assert.That(resolvedScriptingRootPath, Is.EqualTo(expectedScriptingRootPath));
        }

        [Test]
        public void ResolveScriptingRootPath_WhenKnownLayoutsAreMissing_ShouldDiscoverNestedCompilerLayout()
        {
            string contentsPath = CreateDirectory("Contents");
            string expectedScriptingRootPath = CreateDirectory(Path.Combine("Contents", "PlaybackEngines", "Custom", "Scripting"));
            CreateDirectory(Path.Combine("Contents", "PlaybackEngines", "Custom", "Scripting", "NetCoreRuntime"));
            CreateDirectory(Path.Combine("Contents", "PlaybackEngines", "Custom", "Scripting", "DotNetSdkRoslyn"));
            CreateFile(Path.Combine("Contents", "PlaybackEngines", "Custom", "Scripting", "DotNetSdkRoslyn", "csc.dll"));

            string resolvedScriptingRootPath = ExternalCompilerPathResolver.ResolveScriptingRootPath(contentsPath);

            Assert.That(resolvedScriptingRootPath, Is.EqualTo(expectedScriptingRootPath));
        }

        [Test]
        public void ResolveCompilerDirectoryPath_WhenLegacyLayoutExists_ShouldReturnDotNetSdkRoslynPath()
        {
            // Verifies legacy compiler roots keep resolving to DotNetSdkRoslyn.
            string scriptingRootPath = CreateDirectory("Scripting");
            string expectedCompilerDirectoryPath = CreateDirectory(Path.Combine("Scripting", "DotNetSdkRoslyn"));
            CreateFile(Path.Combine("Scripting", "DotNetSdkRoslyn", "csc.dll"));

            string resolvedCompilerDirectoryPath = ExternalCompilerPathResolver.ResolveCompilerDirectoryPath(scriptingRootPath);

            Assert.That(resolvedCompilerDirectoryPath, Is.EqualTo(expectedCompilerDirectoryPath));
        }

        [Test]
        public void ResolveCompilerDirectoryPath_WhenLegacyLayoutIsIncomplete_ShouldUseDotNetSdkLayout()
        {
            // Verifies stale legacy compiler roots fall back to the versioned DotNetSdk layout.
            string scriptingRootPath = CreateDirectory("Scripting");
            CreateDirectory(Path.Combine("Scripting", "DotNetSdkRoslyn"));
            string expectedCompilerDirectoryPath = CreateDirectory(Path.Combine("Scripting", "DotNetSdk", "sdk", "8.0.318", "Roslyn", "bincore"));

            string resolvedCompilerDirectoryPath = ExternalCompilerPathResolver.ResolveCompilerDirectoryPath(scriptingRootPath);

            Assert.That(resolvedCompilerDirectoryPath, Is.EqualTo(expectedCompilerDirectoryPath));
        }

        [Test]
        public void ResolveCompilerDirectoryPath_WhenDotNetSdkLayoutHasMultipleSdkVersions_ShouldChooseHighestSdkRoslynBincorePath()
        {
            // Verifies Unity 6.5 SDK layouts choose the newest versioned Roslyn compiler directory.
            string scriptingRootPath = CreateDirectory("Scripting");
            CreateDirectory(Path.Combine("Scripting", "DotNetSdk", "sdk", "8.0.100", "Roslyn", "bincore"));
            string expectedCompilerDirectoryPath = CreateDirectory(Path.Combine("Scripting", "DotNetSdk", "sdk", "8.0.318", "Roslyn", "bincore"));
            CreateDirectory(Path.Combine("Scripting", "DotNetSdk", "sdk", "current", "Roslyn", "bincore"));

            string resolvedCompilerDirectoryPath = ExternalCompilerPathResolver.ResolveCompilerDirectoryPath(scriptingRootPath);

            Assert.That(resolvedCompilerDirectoryPath, Is.EqualTo(expectedCompilerDirectoryPath));
        }

        [Test]
        public void ShouldUseSharedWorker_WhenContentsRootLegacyRoslynLayoutIsResolved_ShouldReturnFalse()
        {
            // Verifies Unity 2022-style compiler roots skip the shared worker to avoid long Busy stalls.
            ExternalCompilerPaths compilerPaths = CreateCompilerPaths(
                ExternalCompilerLayoutKind.ContentsRootDotNetSdkRoslyn);

            bool shouldUseSharedWorker = RoslynCompilerBackend.ShouldUseSharedWorkerForTests(compilerPaths);

            Assert.That(shouldUseSharedWorker, Is.False);
        }

        [Test]
        public void ShouldUseSharedWorker_WhenResourcesScriptingLayoutIsResolved_ShouldReturnTrue()
        {
            // Verifies Unity 6-style compiler roots keep the shared worker optimization.
            ExternalCompilerPaths compilerPaths = CreateCompilerPaths(
                ExternalCompilerLayoutKind.ResourcesScripting);

            bool shouldUseSharedWorker = RoslynCompilerBackend.ShouldUseSharedWorkerForTests(compilerPaths);

            Assert.That(shouldUseSharedWorker, Is.True);
        }

        [Test]
        public void ReportSharedWorkerSkipped_WhenContentsRootLegacyLayoutIsSkipped_ShouldNotEmitConsoleError()
        {
            // Verifies the intentional Unity 2022 legacy-layout skip is not reported as a Unity Console error.
            DynamicCompilationHealthMonitor.ResetForTests();

            DynamicCompilationHealthMonitor.ReportSharedWorkerSkipped(
                "contents_root_legacy_layout",
                new { layout_kind = ExternalCompilerLayoutKind.ContentsRootDotNetSdkRoslyn.ToString() });

            LogAssert.NoUnexpectedReceived();
        }

        private string CreateDirectory(string relativePath)
        {
            string directoryPath = Path.Combine(_tempDirectoryPath, relativePath);
            Directory.CreateDirectory(directoryPath);
            return directoryPath;
        }

        private string CreateFile(string relativePath)
        {
            string filePath = Path.Combine(_tempDirectoryPath, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(filePath));
            File.WriteAllText(filePath, string.Empty);
            return filePath;
        }

        private ExternalCompilerPaths CreateCompilerPaths(ExternalCompilerLayoutKind layoutKind)
        {
            string contentsPath = CreateDirectory("CompilerPaths");
            string scriptingRelativePath = layoutKind == ExternalCompilerLayoutKind.ContentsRootDotNetSdkRoslyn
                ? "CompilerPaths"
                : Path.Combine("CompilerPaths", "Resources", "Scripting");
            string scriptingRootPath = layoutKind == ExternalCompilerLayoutKind.ContentsRootDotNetSdkRoslyn
                ? contentsPath
                : CreateDirectory(scriptingRelativePath);
            string compilerDirectoryPath = CreateDirectory(Path.Combine(scriptingRelativePath, "DotNetSdkRoslyn"));
            string runtimeDirectoryPath = CreateDirectory(Path.Combine(
                scriptingRelativePath,
                "NetCoreRuntime",
                "shared",
                "Microsoft.NETCore.App",
                "6.0.0"));
            return new ExternalCompilerPaths(
                contentsPath,
                scriptingRootPath,
                Path.Combine(scriptingRootPath, "NetCoreRuntime", "dotnet"),
                Path.Combine(compilerDirectoryPath, "csc.dll"),
                Path.Combine(compilerDirectoryPath, "csc.runtimeconfig.json"),
                Path.Combine(compilerDirectoryPath, "csc.deps.json"),
                Path.Combine(compilerDirectoryPath, "Microsoft.CodeAnalysis.dll"),
                Path.Combine(compilerDirectoryPath, "Microsoft.CodeAnalysis.CSharp.dll"),
                runtimeDirectoryPath,
                layoutKind);
        }
    }
}
