using System.IO;
using NUnit.Framework;

namespace io.github.hatayama.uLoopMCP.DynamicCodeToolTests
{
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

            string resolvedScriptingRootPath = ExternalCompilerPathResolver.ResolveScriptingRootPath(contentsPath);

            Assert.That(resolvedScriptingRootPath, Is.EqualTo(contentsPath));
        }

        [Test]
        public void ResolveScriptingRootPath_WhenResourcesScriptingLayoutExists_ShouldReturnResourcesScriptingPath()
        {
            string contentsPath = CreateDirectory("Contents");
            string expectedScriptingRootPath = CreateDirectory(Path.Combine("Contents", "Resources", "Scripting"));
            CreateDirectory(Path.Combine("Contents", "Resources", "Scripting", "NetCoreRuntime"));
            CreateDirectory(Path.Combine("Contents", "Resources", "Scripting", "DotNetSdkRoslyn"));

            string resolvedScriptingRootPath = ExternalCompilerPathResolver.ResolveScriptingRootPath(contentsPath);

            Assert.That(resolvedScriptingRootPath, Is.EqualTo(expectedScriptingRootPath));
        }

        [Test]
        public void ResolveScriptingRootPath_WhenBothLayoutsExist_ShouldPreferResourcesScriptingLayout()
        {
            string contentsPath = CreateDirectory("Contents");
            CreateDirectory(Path.Combine("Contents", "NetCoreRuntime"));
            CreateDirectory(Path.Combine("Contents", "DotNetSdkRoslyn"));
            string expectedScriptingRootPath = CreateDirectory(Path.Combine("Contents", "Resources", "Scripting"));
            CreateDirectory(Path.Combine("Contents", "Resources", "Scripting", "NetCoreRuntime"));
            CreateDirectory(Path.Combine("Contents", "Resources", "Scripting", "DotNetSdkRoslyn"));

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

            string resolvedScriptingRootPath = ExternalCompilerPathResolver.ResolveScriptingRootPath(contentsPath);

            Assert.That(resolvedScriptingRootPath, Is.EqualTo(expectedScriptingRootPath));
        }

        private string CreateDirectory(string relativePath)
        {
            string directoryPath = Path.Combine(_tempDirectoryPath, relativePath);
            Directory.CreateDirectory(directoryPath);
            return directoryPath;
        }
    }
}
