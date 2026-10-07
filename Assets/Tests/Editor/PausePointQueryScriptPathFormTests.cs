using System;

using Newtonsoft.Json.Linq;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.Infrastructure;
using io.github.hatayama.UnityCliLoop.Runtime;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies that status, extend, and clear reach a source pause point whichever path form the
    /// query names its file by, while a named marker is looked up by its id as given.
    /// </summary>
    public sealed class PausePointQueryScriptPathFormTests
    {
        // An embedded package whose folder name differs from its package name, so the folder path
        // and the asset path of the same script differ.
        private const string PackageAssetPath =
            "Packages/io.github.hatayama.uloop.hotreload-package-fixture/Runtime/HotReloadPackageFixture.cs";
        private const string PackagePhysicalPath =
            "Packages/uloop-hotreload-package-fixture/Runtime/HotReloadPackageFixture.cs";
        private const string AssetsScriptPath = "Assets/Tests/Editor/PausePointQueryScriptPathFormTests.cs";
        private const string Line = ":16";
        private const string ProjectRootToken = "<root>/";

        [SetUp]
        public void SetUp()
        {
            UloopPausePointRegistry.ConfigureForTests(new FakePauseController(), () => DateTime.UtcNow);
        }

        [TearDown]
        public void TearDown()
        {
            UloopPausePointRegistry.ResetForTests();
        }

        /// <summary>
        /// What: a status query naming the file by its folder path, its absolute path, or a ./ path
        /// finds the marker enabled under the asset path.
        /// </summary>
        [TestCase(PackageAssetPath, PackagePhysicalPath)]
        [TestCase(PackageAssetPath, ProjectRootToken + PackagePhysicalPath)]
        [TestCase(PackageAssetPath, "./" + PackagePhysicalPath)]
        [TestCase(PackageAssetPath, "./" + PackageAssetPath)]
        [TestCase(AssetsScriptPath, "./" + AssetsScriptPath)]
        [TestCase(AssetsScriptPath, ProjectRootToken + AssetsScriptPath)]
        public void Status_ByAnotherPathForm_FindsTheMarkerEnabledByTheAssetPath(string enabledPath, string queriedPath)
        {
            string markerId = enabledPath + Line;
            UloopPausePointRegistry.Enable(markerId, 30);

            PausePointStatusResponse response = PausePointStatusBridgeCommand.Execute(IdParams(queriedPath + Line));

            Assert.That(response.Status, Is.EqualTo(UloopPausePointStatus.Enabled));
            Assert.That(response.Id, Is.EqualTo(markerId));
        }

        /// <summary>
        /// What: the extend an await sends names the file as the user typed it and still extends the marker.
        /// </summary>
        [Test]
        public void Extend_ByTheFolderPath_ExtendsTheMarkerEnabledByTheAssetPath()
        {
            string markerId = PackageAssetPath + Line;
            UloopPausePointRegistry.Enable(markerId, 1);
            JObject parameters = IdParams(PackagePhysicalPath + Line);
            parameters["MinimumRemainingSeconds"] = 60;

            PausePointStatusResponse response = PausePointStatusBridgeCommand.Extend(parameters);

            Assert.That(response.Id, Is.EqualTo(markerId));
            Assert.That(response.RemainingMilliseconds, Is.GreaterThan(30000));
        }

        /// <summary>
        /// What: the clear sent after an await times out removes the marker when it names the folder path.
        /// </summary>
        [Test]
        public void BridgeClear_ByTheFolderPath_ClearsTheMarkerEnabledByTheAssetPath()
        {
            string markerId = PackageAssetPath + Line;
            UloopPausePointRegistry.Enable(markerId, 30);

            PausePointStatusBridgeCommand.Clear(IdParams(PackagePhysicalPath + Line));

            Assert.That(UloopPausePointRegistry.IsArmed(markerId), Is.False);
        }

        /// <summary>
        /// What: clear-pause-point --file with the folder path removes the marker enabled under the asset path.
        /// </summary>
        [Test]
        public void ToolClear_ByTheFolderPath_ClearsTheMarkerEnabledByTheAssetPath()
        {
            string markerId = PackageAssetPath + Line;
            UloopPausePointRegistry.Enable(markerId, 30);

            new PausePointUseCase().Clear(new ClearPausePointSchema { Id = PackagePhysicalPath + Line });

            Assert.That(UloopPausePointRegistry.IsArmed(markerId), Is.False);
        }

        /// <summary>
        /// What: a named marker whose id is not a file and line is looked up by the id as given, even
        /// when the id reads like a path a rewrite would change.
        /// </summary>
        [Test]
        public void Status_NamedMarker_IsLookedUpByItsIdAsGiven()
        {
            const string markerId = "./jump";
            UloopPausePointRegistry.Enable(markerId, 30);

            PausePointStatusResponse response = PausePointStatusBridgeCommand.Execute(IdParams(markerId));

            Assert.That(response.Status, Is.EqualTo(UloopPausePointStatus.Enabled));
            Assert.That(response.Id, Is.EqualTo(markerId));
        }

        private static JObject IdParams(string id)
        {
            string resolved = id.StartsWith(ProjectRootToken, StringComparison.Ordinal)
                ? UnityCliLoopPathResolver.GetProjectRoot().Replace('\\', '/').TrimEnd('/') + "/"
                    + id.Substring(ProjectRootToken.Length)
                : id;
            return new JObject { ["Id"] = resolved };
        }

        private sealed class FakePauseController : IUloopPausePointPauseController
        {
            public bool IsPlaying => true;
            public bool IsPaused => false;

            public void Pause()
            {
            }

            public void Resume()
            {
            }
        }
    }
}
