using Newtonsoft.Json.Linq;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.Infrastructure;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Tests compile status bridge responses without invoking Unity's real compiler.
    /// </summary>
    [TestFixture]
    public sealed class CompileStatusBridgeCommandTests
    {
        private UnityCliLoopEditorSessionStateService _sessionStateService;
        private UnityCliLoopEditorSessionStateSnapshot _originalSnapshot;

        [SetUp]
        public void SetUp()
        {
            _sessionStateService = UnityCliLoopEditorSessionStateTestFactory.CreateService();
            _originalSnapshot = UnityCliLoopEditorSessionStateTestFactory.CaptureSnapshot(_sessionStateService);
            _sessionStateService.ClearAll();
        }

        [TearDown]
        public void TearDown()
        {
            _originalSnapshot.Restore(_sessionStateService);
        }

        [Test]
        public void BuildResponse_WhenUnityIsIdleAndResultMatches_ReturnsReadyResult()
        {
            // Verifies the CLI can retrieve the compile result once Unity has stopped compiling and reloading.
            _sessionStateService.StoreCompileResult(
                "compile_test_request",
                forceRecompile: false,
                resultJson: "{\"Success\":true}",
                completedAtUtc: System.DateTime.UtcNow);

            GetCompileStatusResponse response = CompileStatusBridgeCommand.BuildResponse(
                "compile_test_request",
                isCompiling: false,
                isUpdating: false,
                isDomainReloadInProgress: false,
                _sessionStateService);

            Assert.That(response.Ready, Is.True);
            Assert.That(response.HasResult, Is.True);
            Assert.That(response.Result, Is.Not.Null);
            Assert.That(response.Result["Success"]?.Value<bool>(), Is.True);
        }

        [Test]
        public void BuildResponse_WhenUnityIsStillCompiling_ReturnsNotReadyWithStoredResult()
        {
            // Verifies compile status does not release the result before Unity can accept follow-up commands.
            _sessionStateService.StoreCompileResult(
                "compile_test_request",
                forceRecompile: false,
                resultJson: "{\"Success\":true}",
                completedAtUtc: System.DateTime.UtcNow);

            GetCompileStatusResponse response = CompileStatusBridgeCommand.BuildResponse(
                "compile_test_request",
                isCompiling: true,
                isUpdating: false,
                isDomainReloadInProgress: false,
                _sessionStateService);

            Assert.That(response.Ready, Is.False);
            Assert.That(response.HasResult, Is.True);
            Assert.That(response.Result, Is.TypeOf<JObject>());
        }

        [Test]
        public void BuildResponse_WhenRequestIdDiffers_DoesNotReturnStaleResult()
        {
            // Verifies an older compile result cannot satisfy a newer CLI request.
            _sessionStateService.StoreCompileResult(
                "compile_old_request",
                forceRecompile: false,
                resultJson: "{\"Success\":true}",
                completedAtUtc: System.DateTime.UtcNow);

            GetCompileStatusResponse response = CompileStatusBridgeCommand.BuildResponse(
                "compile_new_request",
                isCompiling: false,
                isUpdating: false,
                isDomainReloadInProgress: false,
                _sessionStateService);

            Assert.That(response.Ready, Is.True);
            Assert.That(response.HasResult, Is.False);
            Assert.That(response.Result, Is.Null);
        }

        [Test]
        public void BuildResponse_WhenUnityIsIdleAndPendingRequestHasNoReloadSignal_WaitsForResult()
        {
            // Verifies idle polling cannot complete while the original compile operation is still running.
            _sessionStateService.MarkPendingCompileRequest(
                "compile_test_request",
                forceRecompile: false,
                markedAtUtc: System.DateTime.UtcNow);

            GetCompileStatusResponse response = CompileStatusBridgeCommand.BuildResponse(
                "compile_test_request",
                isCompiling: false,
                isUpdating: false,
                isDomainReloadInProgress: false,
                _sessionStateService);

            Assert.That(response.Ready, Is.True);
            Assert.That(response.HasResult, Is.False);
            Assert.That(response.Result, Is.Null);
            Assert.That(_sessionStateService.GetPendingCompileRequest().HasRequest, Is.True);
        }

        [Test]
        public void BuildResponse_WhenUnityIsIdleAndPendingRequestObservedReload_ReturnsRecoveredResult()
        {
            // Verifies reload recovery returns an indeterminate result after Unity actually started Domain Reload.
            _sessionStateService.MarkPendingCompileRequest(
                "compile_test_request",
                forceRecompile: false,
                markedAtUtc: System.DateTime.UtcNow);
            _sessionStateService.MarkPendingCompileRequestReloadObserved();

            GetCompileStatusResponse response = CompileStatusBridgeCommand.BuildResponse(
                "compile_test_request",
                isCompiling: false,
                isUpdating: false,
                isDomainReloadInProgress: false,
                _sessionStateService);

            Assert.That(response.Ready, Is.True);
            Assert.That(response.HasResult, Is.True);
            Assert.That(response.Result["Success"]?.Type, Is.EqualTo(JTokenType.Null));
            Assert.That(response.Result["ErrorCount"]?.Type, Is.EqualTo(JTokenType.Null));
            Assert.That(response.Result["Warnings"]?.Type, Is.EqualTo(JTokenType.Null));
            Assert.That(response.Result["Message"]?.ToString(), Does.Contain("reloaded scripts"));
            Assert.That(_sessionStateService.GetPendingCompileRequest().HasRequest, Is.False);
        }

        [Test]
        public void BuildResponse_WhenUnityIsBusyAndPendingRequestHasNoResult_WaitsForReadiness()
        {
            // Verifies pending recovery does not publish an indeterminate result before Unity is idle.
            _sessionStateService.MarkPendingCompileRequest(
                "compile_test_request",
                forceRecompile: false,
                markedAtUtc: System.DateTime.UtcNow);

            GetCompileStatusResponse response = CompileStatusBridgeCommand.BuildResponse(
                "compile_test_request",
                isCompiling: true,
                isUpdating: false,
                isDomainReloadInProgress: false,
                _sessionStateService);

            Assert.That(response.Ready, Is.False);
            Assert.That(response.HasResult, Is.False);
            Assert.That(response.Result, Is.Null);
            Assert.That(_sessionStateService.GetPendingCompileRequest().HasRequest, Is.True);
        }

        [Test]
        public void BuildResponse_WhenForceCompilePendingRequestHasNoResult_ReturnsExplanationMessage()
        {
            // Verifies forced compile recovery explains why detailed result fields are null.
            _sessionStateService.MarkPendingCompileRequest(
                "compile_test_request",
                forceRecompile: true,
                markedAtUtc: System.DateTime.UtcNow);
            _sessionStateService.MarkPendingCompileRequestReloadObserved();

            GetCompileStatusResponse response = CompileStatusBridgeCommand.BuildResponse(
                "compile_test_request",
                isCompiling: false,
                isUpdating: false,
                isDomainReloadInProgress: false,
                _sessionStateService);

            Assert.That(response.Ready, Is.True);
            Assert.That(response.HasResult, Is.True);
            Assert.That(response.Result["Success"]?.Type, Is.EqualTo(JTokenType.Null));
            Assert.That(response.Result["ErrorCount"]?.Type, Is.EqualTo(JTokenType.Null));
            Assert.That(
                response.Result["Message"]?.ToString(),
                Is.EqualTo(UnityCliLoopConstants.FORCE_COMPILE_UNKNOWN_RESULT_MESSAGE));
        }
    }
}
