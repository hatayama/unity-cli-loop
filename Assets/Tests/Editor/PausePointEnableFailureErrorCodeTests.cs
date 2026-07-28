using System;
using System.Threading;
using System.Threading.Tasks;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.Runtime;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies validation-failure responses carry machine-readable ErrorCode and RecommendedNextAction.
    /// </summary>
    [TestFixture]
    public sealed class PausePointEnableFailureErrorCodeTests
    {
        private FakePausePointPauseController _pauseController;

        [SetUp]
        public void SetUp()
        {
            _pauseController = new FakePausePointPauseController();
            UloopPausePointRegistry.ConfigureForTests(_pauseController, () => DateTime.UtcNow);
        }

        [TearDown]
        public void TearDown()
        {
            UloopPausePointRegistry.ResetForTests();
        }

        /// <summary>
        /// Verifies a non-positive timeout returns INVALID_ARGUMENT with a non-empty next action.
        /// </summary>
        [Test]
        public async Task Enable_WhenTimeoutSecondsIsZero_ReturnsInvalidArgumentErrorCode()
        {
            EnablePausePointTool tool = new();
            JObject parameters = new()
            {
                ["id"] = "jump",
                ["timeoutSeconds"] = 0
            };

            PausePointResponse response = (PausePointResponse)await tool.ExecuteAsync(parameters, CancellationToken.None);

            Assert.That(response.Success, Is.False);
            Assert.That(response.ErrorCode, Is.EqualTo(SourcePausePointConstants.ErrorCodeInvalidArgument));
            Assert.That(response.RecommendedNextAction, Is.Not.Empty);
        }

        /// <summary>
        /// Verifies specifying both id and file:line returns INVALID_ARGUMENT with a non-empty next action.
        /// </summary>
        [Test]
        public async Task Enable_WhenIdAndFileLineAreBothSpecified_ReturnsInvalidArgumentErrorCode()
        {
            EnablePausePointTool tool = new();
            JObject parameters = new()
            {
                ["id"] = "jump",
                ["file"] = "Assets/Tests/Editor/PausePointToolModeTests.cs",
                ["line"] = 10,
                ["timeoutSeconds"] = 30
            };

            PausePointResponse response = (PausePointResponse)await tool.ExecuteAsync(parameters, CancellationToken.None);

            Assert.That(response.Success, Is.False);
            Assert.That(response.ErrorCode, Is.EqualTo(SourcePausePointConstants.ErrorCodeInvalidArgument));
            Assert.That(response.RecommendedNextAction, Is.Not.Empty);
        }

        /// <summary>
        /// Verifies an unknown capture mode returns INVALID_ARGUMENT with a non-empty next action.
        /// </summary>
        [Test]
        public async Task Enable_WhenModeIsUnknown_ReturnsInvalidArgumentErrorCode()
        {
            EnablePausePointTool tool = new();
            JObject parameters = new()
            {
                ["id"] = "jump",
                ["mode"] = "bogus-mode",
                ["timeoutSeconds"] = 30
            };

            PausePointResponse response = (PausePointResponse)await tool.ExecuteAsync(parameters, CancellationToken.None);

            Assert.That(response.Success, Is.False);
            Assert.That(response.ErrorCode, Is.EqualTo(SourcePausePointConstants.ErrorCodeInvalidArgument));
            Assert.That(response.RecommendedNextAction, Is.Not.Empty);
        }

        /// <summary>
        /// Verifies clear without id or --all returns INVALID_ARGUMENT with a non-empty next action.
        /// </summary>
        [Test]
        public async Task Clear_WhenIdIsEmptyAndAllIsFalse_ReturnsInvalidArgumentErrorCode()
        {
            ClearPausePointTool tool = new();
            JObject parameters = new()
            {
                ["id"] = "",
                ["all"] = false
            };

            PausePointResponse response = (PausePointResponse)await tool.ExecuteAsync(parameters, CancellationToken.None);

            Assert.That(response.Success, Is.False);
            Assert.That(response.ErrorCode, Is.EqualTo(SourcePausePointConstants.ErrorCodeInvalidArgument));
            Assert.That(response.RecommendedNextAction, Is.Not.Empty);
        }

        /// <summary>
        /// Verifies an unresolvable file:line returns PAUSE_POINT_RESOLVE_FAILED with a non-empty next action.
        /// </summary>
        [Test]
        public async Task Enable_WhenFileDoesNotExist_ReturnsResolveFailedErrorCode()
        {
            EnablePausePointTool tool = new();
            JObject parameters = new()
            {
                ["file"] = "Assets/DoesNotExist/NoSuchScript.cs",
                ["line"] = 10,
                ["timeoutSeconds"] = 30
            };

            PausePointResponse response = (PausePointResponse)await tool.ExecuteAsync(parameters, CancellationToken.None);

            Assert.That(response.Success, Is.False);
            Assert.That(response.ErrorCode, Is.EqualTo(SourcePausePointConstants.ErrorCodeResolveFailed));
            Assert.That(response.RecommendedNextAction, Is.Not.Empty);
        }

        /// <summary>
        /// Verifies ErrorCode serializes under the exact wire name callers will match.
        /// </summary>
        [Test]
        public void PausePointResponse_WhenErrorCodeIsSet_SerializesErrorCodeWireName()
        {
            // Why production settings: JsonRpcResponseFactory uses these settings; a bare
            // SerializeObject would miss ContractResolver renames and give false confidence.
            string json = JsonConvert.SerializeObject(
                new PausePointResponse { Success = false, ErrorCode = "X" },
                Formatting.None,
                UnityCliLoopJsonResponseSerializerSettings.Settings);

            Assert.That(json, Does.Contain("\"ErrorCode\":\"X\""));
        }

        private sealed class FakePausePointPauseController : IUloopPausePointPauseController
        {
            public int PauseCount { get; private set; }
            public bool IsPlaying => true;
            public bool IsPaused => PauseCount > 0;

            public void Pause()
            {
                PauseCount++;
            }

            public void Resume()
            {
                // Why zero: Unity's isPaused is a bool; Option B Resume must fully clear pause.
                PauseCount = 0;
            }
        }
    }
}
