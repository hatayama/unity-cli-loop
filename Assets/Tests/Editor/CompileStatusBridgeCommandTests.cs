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
        private UnityCliLoopCompileSessionLifecycleService _compileSessionLifecycleService;
        private UnityCliLoopCompileResultSessionRepository _compileResultSessionRepository;
        private UnityCliLoopPendingCompileSessionRepository _pendingCompileSessionRepository;
        private UnityCliLoopEditorSessionStateSnapshot _originalSnapshot;

        [SetUp]
        public void SetUp()
        {
            _compileSessionLifecycleService =
                UnityCliLoopEditorSessionStateTestFactory.CreateCompileSessionLifecycleService();
            _compileResultSessionRepository =
                UnityCliLoopEditorSessionStateTestFactory.CreateCompileResultSessionRepository();
            _pendingCompileSessionRepository =
                UnityCliLoopEditorSessionStateTestFactory.CreatePendingCompileSessionRepository();
            _originalSnapshot = UnityCliLoopEditorSessionStateTestFactory.CaptureSnapshot();
            UnityCliLoopEditorSessionStateTestFactory.ClearAll();
        }

        [TearDown]
        public void TearDown()
        {
            _originalSnapshot.Restore();
        }

        [Test]
        public void BuildResponse_WhenUnityIsIdleAndResultMatches_ReturnsReadyResult()
        {
            // Verifies the CLI can retrieve the compile result once Unity has stopped compiling and reloading.
            _compileResultSessionRepository.StoreCompileResult(
                "compile_test_request",
                forceRecompile: false,
                resultJson: "{\"Success\":true}",
                completedAtUtc: System.DateTime.UtcNow);

            GetCompileStatusResponse response = CompileStatusBridgeCommand.BuildResponse(
                "compile_test_request",
                isCompiling: false,
                isUpdating: false,
                isDomainReloadInProgress: false,
                _compileSessionLifecycleService,
                _compileResultSessionRepository,
                _pendingCompileSessionRepository);

            Assert.That(response.Ready, Is.True);
            Assert.That(response.HasResult, Is.True);
            Assert.That(response.Result, Is.Not.Null);
            Assert.That(response.Result["Success"]?.Value<bool>(), Is.True);
        }

        [Test]
        public void BuildResponse_WhenUnityIsStillCompiling_ReturnsNotReadyWithStoredResult()
        {
            // Verifies compile status does not release the result before Unity can accept follow-up commands.
            _compileResultSessionRepository.StoreCompileResult(
                "compile_test_request",
                forceRecompile: false,
                resultJson: "{\"Success\":true}",
                completedAtUtc: System.DateTime.UtcNow);

            GetCompileStatusResponse response = CompileStatusBridgeCommand.BuildResponse(
                "compile_test_request",
                isCompiling: true,
                isUpdating: false,
                isDomainReloadInProgress: false,
                _compileSessionLifecycleService,
                _compileResultSessionRepository,
                _pendingCompileSessionRepository);

            Assert.That(response.Ready, Is.False);
            Assert.That(response.HasResult, Is.True);
            Assert.That(response.Result, Is.TypeOf<JObject>());
        }

        [Test]
        public void BuildResponse_WhenRequestIdDiffers_DoesNotReturnStaleResult()
        {
            // Verifies an older compile result cannot satisfy a newer CLI request.
            _compileResultSessionRepository.StoreCompileResult(
                "compile_old_request",
                forceRecompile: false,
                resultJson: "{\"Success\":true}",
                completedAtUtc: System.DateTime.UtcNow);

            GetCompileStatusResponse response = CompileStatusBridgeCommand.BuildResponse(
                "compile_new_request",
                isCompiling: false,
                isUpdating: false,
                isDomainReloadInProgress: false,
                _compileSessionLifecycleService,
                _compileResultSessionRepository,
                _pendingCompileSessionRepository);

            Assert.That(response.Ready, Is.True);
            Assert.That(response.HasResult, Is.False);
            Assert.That(response.Result, Is.Null);
        }

        [Test]
        public void BuildResponse_WhenUnityIsIdleAndPendingRequestHasNoReloadSignal_WaitsForResult()
        {
            // Verifies idle polling cannot complete while the original compile operation is still running.
            _compileSessionLifecycleService.MarkPendingCompileRequest(
                "compile_test_request",
                forceRecompile: false,
                markedAtUtc: System.DateTime.UtcNow);

            GetCompileStatusResponse response = CompileStatusBridgeCommand.BuildResponse(
                "compile_test_request",
                isCompiling: false,
                isUpdating: false,
                isDomainReloadInProgress: false,
                _compileSessionLifecycleService,
                _compileResultSessionRepository,
                _pendingCompileSessionRepository);

            Assert.That(response.Ready, Is.True);
            Assert.That(response.HasResult, Is.False);
            Assert.That(response.Result, Is.Null);
            Assert.That(GetPendingCompileRequest().HasRequest, Is.True);
        }

        [Test]
        public void BuildResponse_WhenUnityIsIdleAndPendingRequestObservedReload_ReturnsRecoveredResult()
        {
            // Verifies reload recovery returns an indeterminate result after Unity actually started Domain Reload.
            _compileSessionLifecycleService.MarkPendingCompileRequest(
                "compile_test_request",
                forceRecompile: false,
                markedAtUtc: System.DateTime.UtcNow);
            _pendingCompileSessionRepository.MarkPendingCompileRequestReloadObserved();

            GetCompileStatusResponse response = CompileStatusBridgeCommand.BuildResponse(
                "compile_test_request",
                isCompiling: false,
                isUpdating: false,
                isDomainReloadInProgress: false,
                _compileSessionLifecycleService,
                _compileResultSessionRepository,
                _pendingCompileSessionRepository);

            Assert.That(response.Ready, Is.True);
            Assert.That(response.HasResult, Is.True);
            Assert.That(response.Result["Success"]?.Type, Is.EqualTo(JTokenType.Null));
            Assert.That(response.Result["ErrorCount"]?.Type, Is.EqualTo(JTokenType.Null));
            Assert.That(response.Result["Warnings"]?.Type, Is.EqualTo(JTokenType.Null));
            Assert.That(response.Result["Message"]?.ToString(), Does.Contain("reloaded scripts"));
            Assert.That(GetPendingCompileRequest().HasRequest, Is.False);
        }

        [Test]
        public void BuildResponse_WhenUnityIsBusyAndPendingRequestHasNoResult_WaitsForReadiness()
        {
            // Verifies pending recovery does not publish an indeterminate result before Unity is idle.
            _compileSessionLifecycleService.MarkPendingCompileRequest(
                "compile_test_request",
                forceRecompile: false,
                markedAtUtc: System.DateTime.UtcNow);

            GetCompileStatusResponse response = CompileStatusBridgeCommand.BuildResponse(
                "compile_test_request",
                isCompiling: true,
                isUpdating: false,
                isDomainReloadInProgress: false,
                _compileSessionLifecycleService,
                _compileResultSessionRepository,
                _pendingCompileSessionRepository);

            Assert.That(response.Ready, Is.False);
            Assert.That(response.HasResult, Is.False);
            Assert.That(response.Result, Is.Null);
            Assert.That(GetPendingCompileRequest().HasRequest, Is.True);
        }

        [Test]
        public void BuildResponse_WhenForceCompilePendingRequestHasNoResult_ReturnsExplanationMessage()
        {
            // Verifies forced compile recovery explains why detailed result fields are null.
            _compileSessionLifecycleService.MarkPendingCompileRequest(
                "compile_test_request",
                forceRecompile: true,
                markedAtUtc: System.DateTime.UtcNow);
            _pendingCompileSessionRepository.MarkPendingCompileRequestReloadObserved();

            GetCompileStatusResponse response = CompileStatusBridgeCommand.BuildResponse(
                "compile_test_request",
                isCompiling: false,
                isUpdating: false,
                isDomainReloadInProgress: false,
                _compileSessionLifecycleService,
                _compileResultSessionRepository,
                _pendingCompileSessionRepository);

            Assert.That(response.Ready, Is.True);
            Assert.That(response.HasResult, Is.True);
            Assert.That(response.Result["Success"]?.Type, Is.EqualTo(JTokenType.Null));
            Assert.That(response.Result["ErrorCount"]?.Type, Is.EqualTo(JTokenType.Null));
            Assert.That(
                response.Result["Message"]?.ToString(),
                Is.EqualTo(ForceCompileUnknownResult.MessageText));
        }

        private UnityCliLoopPendingCompileRequest GetPendingCompileRequest()
        {
            UnityCliLoopPendingCompileRequest[] pendingRequests =
                _pendingCompileSessionRepository.GetPendingCompileRequests();
            if (pendingRequests.Length == 0)
            {
                return UnityCliLoopPendingCompileRequest.None();
            }

            return pendingRequests[0];
        }
    }
}
