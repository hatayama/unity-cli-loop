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
        private DomainReloadDurationMonitor _reloadDurationMonitor;
        private bool _isInPlayMode;

        [SetUp]
        public void SetUp()
        {
            _compileSessionLifecycleService =
                UnityCliLoopEditorSessionStateTestFactory.CreateCompileSessionLifecycleService();
            _compileResultSessionRepository =
                UnityCliLoopEditorSessionStateTestFactory.CreateCompileResultSessionRepository();
            _pendingCompileSessionRepository =
                UnityCliLoopEditorSessionStateTestFactory.CreatePendingCompileSessionRepository();
            _isInPlayMode = false;
            _reloadDurationMonitor = new DomainReloadDurationMonitor(
                new InMemoryDomainReloadDurationStore(),
                () => _isInPlayMode);
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
                completedAtUtc: System.DateTime.UtcNow,
                isFollowedByReload: false);

            GetCompileStatusResponse response = CompileStatusBridgeCommand.BuildResponse(
                "compile_test_request",
                isCompiling: false,
                isUpdating: false,
                isDomainReloadInProgress: false,
                isSnapshotCaptureHolding: false,
                compileStartPendingSeconds: 0d,
                _compileSessionLifecycleService,
                _compileResultSessionRepository,
                _pendingCompileSessionRepository,
                _reloadDurationMonitor);

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
                completedAtUtc: System.DateTime.UtcNow,
                isFollowedByReload: false);

            GetCompileStatusResponse response = CompileStatusBridgeCommand.BuildResponse(
                "compile_test_request",
                isCompiling: true,
                isUpdating: false,
                isDomainReloadInProgress: false,
                isSnapshotCaptureHolding: false,
                compileStartPendingSeconds: 0d,
                _compileSessionLifecycleService,
                _compileResultSessionRepository,
                _pendingCompileSessionRepository,
                _reloadDurationMonitor);

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
                completedAtUtc: System.DateTime.UtcNow,
                isFollowedByReload: false);

            GetCompileStatusResponse response = CompileStatusBridgeCommand.BuildResponse(
                "compile_new_request",
                isCompiling: false,
                isUpdating: false,
                isDomainReloadInProgress: false,
                isSnapshotCaptureHolding: false,
                compileStartPendingSeconds: 0d,
                _compileSessionLifecycleService,
                _compileResultSessionRepository,
                _pendingCompileSessionRepository,
                _reloadDurationMonitor);

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
                isSnapshotCaptureHolding: false,
                compileStartPendingSeconds: 0d,
                _compileSessionLifecycleService,
                _compileResultSessionRepository,
                _pendingCompileSessionRepository,
                _reloadDurationMonitor);

            Assert.That(response.Ready, Is.True);
            Assert.That(response.HasResult, Is.False);
            Assert.That(response.Result, Is.Null);
            Assert.That(
                UnityCliLoopEditorSessionStateTestFactory.GetSinglePendingCompileRequest(
                    _pendingCompileSessionRepository).HasRequest,
                Is.True);
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
                isSnapshotCaptureHolding: false,
                compileStartPendingSeconds: 0d,
                _compileSessionLifecycleService,
                _compileResultSessionRepository,
                _pendingCompileSessionRepository,
                _reloadDurationMonitor);

            Assert.That(response.Ready, Is.True);
            Assert.That(response.HasResult, Is.True);
            Assert.That(response.Result["Success"]?.Value<bool>(), Is.False);
            Assert.That(response.Result["ErrorCount"]?.Type, Is.EqualTo(JTokenType.Null));
            Assert.That(response.Result["Warnings"]?.Type, Is.EqualTo(JTokenType.Null));
            Assert.That(response.Result["Message"]?.ToString(), Does.Contain("reloaded scripts"));
            Assert.That(
                UnityCliLoopEditorSessionStateTestFactory.GetSinglePendingCompileRequest(
                    _pendingCompileSessionRepository).HasRequest,
                Is.False);
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
                isSnapshotCaptureHolding: false,
                compileStartPendingSeconds: 0d,
                _compileSessionLifecycleService,
                _compileResultSessionRepository,
                _pendingCompileSessionRepository,
                _reloadDurationMonitor);

            Assert.That(response.Ready, Is.False);
            Assert.That(response.HasResult, Is.False);
            Assert.That(response.Result, Is.Null);
            Assert.That(
                UnityCliLoopEditorSessionStateTestFactory.GetSinglePendingCompileRequest(
                    _pendingCompileSessionRepository).HasRequest,
                Is.True);
        }

        [Test]
        public void BuildResponse_WhileTheSnapshotCaptureHolds_IsNotReadyAndDoesNotRecoverAPendingResult()
        {
            // Verifies an idle Unity still withholds the compile completion, and leaves the pending
            // request for a later poll, while the hot reload source snapshot capture is running.
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
                isSnapshotCaptureHolding: true,
                compileStartPendingSeconds: 0d,
                _compileSessionLifecycleService,
                _compileResultSessionRepository,
                _pendingCompileSessionRepository,
                _reloadDurationMonitor);

            Assert.That(response.Ready, Is.False);
            Assert.That(response.HasResult, Is.False);
            Assert.That(response.Result, Is.Null);
            Assert.That(
                UnityCliLoopEditorSessionStateTestFactory.GetSinglePendingCompileRequest(
                    _pendingCompileSessionRepository).HasRequest,
                Is.True);
        }

        [Test]
        public void BuildResponse_WhenStoredResultIsCompileNotStarted_IsReadyWhileUnityStillReportsCompiling()
        {
            // Verifies a COMPILE_NOT_STARTED abort is released to the CLI even though Unity keeps
            // isCompiling true for the queued compile; otherwise the CLI would wait until its own timeout.
            _compileResultSessionRepository.StoreCompileResult(
                "compile_test_request",
                forceRecompile: false,
                resultJson: "{\"Success\":false,\"ErrorCode\":\"" +
                    CompileLifecycleErrorCodes.CompileNotStartedErrorCodeText + "\"}",
                completedAtUtc: System.DateTime.UtcNow,
                isFollowedByReload: false);

            GetCompileStatusResponse response = CompileStatusBridgeCommand.BuildResponse(
                "compile_test_request",
                isCompiling: true,
                isUpdating: false,
                isDomainReloadInProgress: false,
                isSnapshotCaptureHolding: false,
                compileStartPendingSeconds: 60d,
                _compileSessionLifecycleService,
                _compileResultSessionRepository,
                _pendingCompileSessionRepository,
                _reloadDurationMonitor);

            Assert.That(response.Ready, Is.True);
            Assert.That(response.HasResult, Is.True);
            Assert.That(response.IsCompiling, Is.True);
            Assert.That(
                response.Result["ErrorCode"]?.ToString(),
                Is.EqualTo(CompileLifecycleErrorCodes.CompileNotStartedErrorCodeText));
        }

        [Test]
        public void BuildResponse_WhenStoredResultIsCompileNotStarted_StaysNotReadyWhileEditorIsUpdating()
        {
            // Verifies the not-started result lifts only the isCompiling gate: an asset refresh in
            // progress still holds it back, as it does for every other stored result.
            _compileResultSessionRepository.StoreCompileResult(
                "compile_test_request",
                forceRecompile: false,
                resultJson: "{\"Success\":false,\"ErrorCode\":\"" +
                    CompileLifecycleErrorCodes.CompileNotStartedErrorCodeText + "\"}",
                completedAtUtc: System.DateTime.UtcNow,
                isFollowedByReload: false);

            GetCompileStatusResponse response = CompileStatusBridgeCommand.BuildResponse(
                "compile_test_request",
                isCompiling: true,
                isUpdating: true,
                isDomainReloadInProgress: false,
                isSnapshotCaptureHolding: false,
                compileStartPendingSeconds: 60d,
                _compileSessionLifecycleService,
                _compileResultSessionRepository,
                _pendingCompileSessionRepository,
                _reloadDurationMonitor);

            Assert.That(response.Ready, Is.False);
            Assert.That(response.HasResult, Is.True);
        }

        [Test]
        public void BuildResponse_WhenStoredResultHasAnotherErrorCode_StaysNotReadyWhileCompiling()
        {
            // Verifies only the not-started code bypasses the readiness gate; other stored results still
            // wait for Unity to finish compiling and reloading.
            _compileResultSessionRepository.StoreCompileResult(
                "compile_test_request",
                forceRecompile: false,
                resultJson: "{\"Success\":false,\"ErrorCode\":\"" + ForceCompileUnknownResult.ErrorCodeText + "\"}",
                completedAtUtc: System.DateTime.UtcNow,
                isFollowedByReload: false);

            GetCompileStatusResponse response = CompileStatusBridgeCommand.BuildResponse(
                "compile_test_request",
                isCompiling: true,
                isUpdating: false,
                isDomainReloadInProgress: false,
                isSnapshotCaptureHolding: false,
                compileStartPendingSeconds: 0d,
                _compileSessionLifecycleService,
                _compileResultSessionRepository,
                _pendingCompileSessionRepository,
                _reloadDurationMonitor);

            Assert.That(response.Ready, Is.False);
        }

        [Test]
        public void BuildResponse_PropagatesCompileStartPendingSeconds()
        {
            // Verifies the pending-start seconds reach the CLI so it can bring the Editor to the front.
            GetCompileStatusResponse response = CompileStatusBridgeCommand.BuildResponse(
                "compile_test_request",
                isCompiling: true,
                isUpdating: false,
                isDomainReloadInProgress: false,
                isSnapshotCaptureHolding: false,
                compileStartPendingSeconds: 12.5,
                _compileSessionLifecycleService,
                _compileResultSessionRepository,
                _pendingCompileSessionRepository,
                _reloadDurationMonitor);

            Assert.That(response.CompileStartPendingSeconds, Is.EqualTo(12.5));
            Assert.That(response.Ready, Is.False);
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
                isSnapshotCaptureHolding: false,
                compileStartPendingSeconds: 0d,
                _compileSessionLifecycleService,
                _compileResultSessionRepository,
                _pendingCompileSessionRepository,
                _reloadDurationMonitor);

            Assert.That(response.Ready, Is.True);
            Assert.That(response.HasResult, Is.True);
            Assert.That(response.Result["Success"]?.Value<bool>(), Is.False);
            Assert.That(response.Result["ErrorCount"]?.Type, Is.EqualTo(JTokenType.Null));
            Assert.That(
                response.Result["Message"]?.ToString(),
                Is.EqualTo(ForceCompileUnknownResult.MessageText));
            Assert.That(response.Result["ErrorCode"]?.ToString(), Is.EqualTo(ForceCompileUnknownResult.ErrorCodeText));
            Assert.That(response.Result["NextActions"]?.Type, Is.EqualTo(JTokenType.Array));
        }

        [Test]
        public void BuildResponse_WhenAReloadStartedAfterTheStoredResult_AddsTheReloadSeconds()
        {
            // Verifies a compile result stored before its domain reload reports how long that reload took.
            System.DateTime completedAt = System.DateTime.UtcNow;
            StoreResult("{\"Success\":true,\"ErrorCount\":0}", completedAt, isFollowedByReload: true);
            RecordReload(completedAt.AddMilliseconds(10), 1.5d);

            GetCompileStatusResponse response = BuildIdleResponse();

            Assert.That(response.Result["DomainReloadSeconds"]?.Value<double>(), Is.EqualTo(1.5d));
        }

        [Test]
        public void BuildResponse_WhenNoReloadFollowedTheStoredResult_ReportsNoReload()
        {
            // Verifies a compile whose reload never came does not report an earlier reload once Unity is ready.
            System.DateTime completedAt = System.DateTime.UtcNow;
            RecordReload(completedAt.AddSeconds(-3), 2d);
            StoreResult("{\"Success\":true}", completedAt, isFollowedByReload: true);

            GetCompileStatusResponse response = BuildIdleResponse();

            Assert.That(response.Result["DomainReloadSeconds"]?.Type, Is.EqualTo(JTokenType.Null));
        }

        [Test]
        public void BuildResponse_WhenReadyWithoutAReload_KeepsReportingNoReloadAfterALaterReload()
        {
            // Verifies a ready read with no reload after the result settles null, so a later reload with another
            // cause is not given to this compile.
            System.DateTime completedAt = System.DateTime.UtcNow.AddSeconds(-30);
            StoreResult("{\"Success\":true}", completedAt, isFollowedByReload: true);

            GetCompileStatusResponse firstResponse = BuildIdleResponse();
            RecordReload(completedAt.AddSeconds(10), 2d);
            GetCompileStatusResponse secondResponse = BuildIdleResponse();

            Assert.That(firstResponse.Result["DomainReloadSeconds"]?.Type, Is.EqualTo(JTokenType.Null));
            Assert.That(secondResponse.Result["DomainReloadSeconds"]?.Type, Is.EqualTo(JTokenType.Null));
        }

        [Test]
        public void BuildResponse_WhenTheCompileCausesNoReload_ReportsNoReloadEvenWhenFirstReadAfterALaterReload()
        {
            // Verifies a compile that was skipped, stopped, or had errors does not take a later reload with another
            // cause, even when the Editor is still busy at the first read.
            System.DateTime completedAt = System.DateTime.UtcNow.AddSeconds(-30);
            StoreResult("{\"Success\":true}", completedAt, isFollowedByReload: false);
            RecordReload(completedAt.AddSeconds(10), 2d);

            GetCompileStatusResponse busyResponse = BuildResponseWhileCompiling();

            Assert.That(busyResponse.Result["DomainReloadSeconds"]?.Type, Is.EqualTo(JTokenType.Null));
        }

        [Test]
        public void BuildResponse_WhenReadAgainAfterAnotherReload_KeepsTheFirstReportedReload()
        {
            // Verifies the reload is settled and stored, so a later reload does not replace it, the result keeps
            // its original completion time, and it still says a reload followed.
            System.DateTime completedAt = System.DateTime.UtcNow.AddSeconds(-30);
            StoreResult("{\"Success\":true}", completedAt, isFollowedByReload: true);
            RecordReload(completedAt.AddSeconds(1), 1.5d);

            GetCompileStatusResponse firstResponse = BuildIdleResponse();
            RecordReload(completedAt.AddSeconds(10), 4d);
            GetCompileStatusResponse secondResponse = BuildIdleResponse();

            UnityCliLoopStoredCompileResult stored =
                _compileResultSessionRepository.GetCompileResult("compile_test_request");
            Assert.That(firstResponse.Result["DomainReloadSeconds"]?.Value<double>(), Is.EqualTo(1.5d));
            Assert.That(secondResponse.Result["DomainReloadSeconds"]?.Value<double>(), Is.EqualTo(1.5d));
            Assert.That(stored.CompletedAtUtcTicks, Is.EqualTo(completedAt.Ticks));
            Assert.That(stored.IsFollowedByReload, Is.True);
        }

        [Test]
        public void BuildResponse_WhenTwoReloadsFinishBeforeTheFirstRead_ReportsTheFirst()
        {
            // Verifies a result first read after an unrelated later reload, as when a timed-out CLI collects it,
            // reports the reload this compile caused.
            System.DateTime completedAt = System.DateTime.UtcNow.AddSeconds(-30);
            StoreResult("{\"Success\":true}", completedAt, isFollowedByReload: true);
            RecordReload(completedAt.AddSeconds(1), 1.5d);
            RecordReload(completedAt.AddSeconds(10), 4d);

            GetCompileStatusResponse response = BuildIdleResponse();

            Assert.That(response.Result["DomainReloadSeconds"]?.Value<double>(), Is.EqualTo(1.5d));
        }

        [Test]
        public void BuildResponse_WhenTheForcedCompileResultIsUnknown_AddsTheReloadSeconds()
        {
            // Verifies a forced recompile, whose result is stored as unknown even when it compiled and reloaded,
            // still reports the reload it caused.
            System.DateTime completedAt = System.DateTime.UtcNow;
            StoreResult(
                "{\"Success\":false,\"ErrorCount\":null,\"ErrorCode\":\"COMPILE_RESULT_UNKNOWN\"}",
                completedAt,
                isFollowedByReload: true);
            RecordReload(completedAt.AddMilliseconds(10), 1.5d);

            GetCompileStatusResponse response = BuildIdleResponse();

            Assert.That(response.Result["DomainReloadSeconds"]?.Value<double>(), Is.EqualTo(1.5d));
        }

        [Test]
        public void BuildResponse_BeforeTheReloadStarts_ReportsNullWithoutSettling()
        {
            // Verifies a poll after the compile stored its result but before its reload reports null without storing
            // it, so the ready read after the reload still reports that reload.
            System.DateTime completedAt = System.DateTime.UtcNow.AddSeconds(-30);
            StoreResult("{\"Success\":true}", completedAt, isFollowedByReload: true);

            GetCompileStatusResponse pollBeforeReload = BuildResponseWhileCompiling();
            RecordReload(completedAt.AddSeconds(1), 2d);
            GetCompileStatusResponse readyResponse = BuildIdleResponse();

            Assert.That(pollBeforeReload.Ready, Is.False);
            Assert.That(pollBeforeReload.Result["DomainReloadSeconds"]?.Type, Is.EqualTo(JTokenType.Null));
            Assert.That(readyResponse.Result["DomainReloadSeconds"]?.Value<double>(), Is.EqualTo(2d));
        }

        [Test]
        public void BuildResponse_WhenNotReadyAfterTheReload_SettlesTheReload()
        {
            // Verifies a read while the Editor is busy with other work after this compile's reload, as when a
            // timed-out CLI collects the result, reports and stores that reload.
            System.DateTime completedAt = System.DateTime.UtcNow.AddSeconds(-30);
            StoreResult("{\"Success\":true}", completedAt, isFollowedByReload: true);
            RecordReload(completedAt.AddSeconds(1), 1.5d);

            GetCompileStatusResponse busyResponse = BuildResponseWhileCompiling();
            GetCompileStatusResponse readyResponse = BuildIdleResponse();

            Assert.That(busyResponse.Ready, Is.False);
            Assert.That(busyResponse.Result["DomainReloadSeconds"]?.Value<double>(), Is.EqualTo(1.5d));
            Assert.That(readyResponse.Result["DomainReloadSeconds"]?.Value<double>(), Is.EqualTo(1.5d));
        }

        [Test]
        public void BuildResponse_WhenRecoveringAResultAfterAReload_AddsTheReloadSeconds()
        {
            // Verifies the result recovered after a reload, which is created after that reload, still reports it.
            System.DateTime markedAt = System.DateTime.UtcNow.AddSeconds(-10);
            MarkPendingRequestThatObservedAReload(markedAt);
            RecordReload(markedAt.AddSeconds(1), 2d);

            GetCompileStatusResponse response = BuildIdleResponse();
            GetCompileStatusResponse secondResponse = BuildIdleResponse();

            Assert.That(response.Result["DomainReloadSeconds"]?.Value<double>(), Is.EqualTo(2d));
            Assert.That(secondResponse.Result["DomainReloadSeconds"]?.Value<double>(), Is.EqualTo(2d));
        }

        [Test]
        public void BuildResponse_WhenRecoveringAfterTwoReloads_ReportsTheFirstSinceTheRequest()
        {
            // Verifies a recovered result first read after an unrelated later reload reports the reload its request
            // observed, not the later one.
            System.DateTime markedAt = System.DateTime.UtcNow.AddSeconds(-30);
            MarkPendingRequestThatObservedAReload(markedAt);
            RecordReload(markedAt.AddSeconds(1), 2d);
            RecordReload(markedAt.AddSeconds(10), 4d);

            GetCompileStatusResponse response = BuildIdleResponse();

            Assert.That(response.Result["DomainReloadSeconds"]?.Value<double>(), Is.EqualTo(2d));
        }

        [Test]
        public void BuildResponse_WhenRecoveringAfterAPlayModeReload_DoesNotReportAnEarlierSlowReload()
        {
            // Verifies a recovered result whose reload ran in Play Mode, and so was not measured, reports no reload
            // and no restart advice instead of an earlier compile's slow reload.
            System.DateTime start = System.DateTime.UtcNow.AddMinutes(-5);
            for (int i = 0; i < 3; i++)
            {
                RecordReload(start.AddSeconds(i * 10), 1d);
            }

            for (int i = 3; i < 13; i++)
            {
                RecordReload(start.AddSeconds(i * 10), 2d);
            }

            System.DateTime markedAt = start.AddSeconds(200);
            MarkPendingRequestThatObservedAReload(markedAt);
            _isInPlayMode = true;
            RecordReload(markedAt.AddSeconds(1), 3d);

            GetCompileStatusResponse response = BuildIdleResponse();

            Assert.That(response.Result["DomainReloadSeconds"]?.Type, Is.EqualTo(JTokenType.Null));
            Assert.That(response.Result["Warning"], Is.Null);
        }

        private GetCompileStatusResponse BuildResponseWhileCompiling()
        {
            return CompileStatusBridgeCommand.BuildResponse(
                "compile_test_request",
                isCompiling: true,
                isUpdating: false,
                isDomainReloadInProgress: false,
                isSnapshotCaptureHolding: false,
                compileStartPendingSeconds: 0d,
                _compileSessionLifecycleService,
                _compileResultSessionRepository,
                _pendingCompileSessionRepository,
                _reloadDurationMonitor);
        }

        private void StoreResult(string resultJson, System.DateTime completedAt, bool isFollowedByReload)
        {
            _compileResultSessionRepository.StoreCompileResult(
                "compile_test_request",
                forceRecompile: false,
                resultJson: resultJson,
                completedAtUtc: completedAt,
                isFollowedByReload: isFollowedByReload);
        }

        private void MarkPendingRequestThatObservedAReload(System.DateTime markedAt)
        {
            _compileSessionLifecycleService.MarkPendingCompileRequest(
                "compile_test_request",
                forceRecompile: false,
                markedAtUtc: markedAt);
            _pendingCompileSessionRepository.MarkPendingCompileRequestReloadObserved();
        }

        private void RecordReload(System.DateTime startedAt, double seconds)
        {
            _reloadDurationMonitor.RecordReloadStarted(startedAt);
            _reloadDurationMonitor.RecordReloadFinished(startedAt.AddSeconds(seconds));
        }

        private GetCompileStatusResponse BuildIdleResponse()
        {
            return CompileStatusBridgeCommand.BuildResponse(
                "compile_test_request",
                isCompiling: false,
                isUpdating: false,
                isDomainReloadInProgress: false,
                isSnapshotCaptureHolding: false,
                compileStartPendingSeconds: 0d,
                _compileSessionLifecycleService,
                _compileResultSessionRepository,
                _pendingCompileSessionRepository,
                _reloadDurationMonitor);
        }
    }
}
