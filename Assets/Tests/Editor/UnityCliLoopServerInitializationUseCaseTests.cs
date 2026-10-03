using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies server initialization validates the editor, starts a server, and records the session.
    /// </summary>
    public sealed class UnityCliLoopServerInitializationUseCaseTests
    {
        /// <summary>
        /// Verifies an invalid editor state fails initialization with the validation message and never creates a server.
        /// </summary>
        [Test]
        public void ExecuteAsync_WhenEditorStateIsInvalid_FailsWithoutCreatingServer()
        {
            RecordingServerInstanceFactory factory = new RecordingServerInstanceFactory();
            RecordingSessionFlagsRepository repository = new RecordingSessionFlagsRepository();
            UnityCliLoopServerInitializationUseCase useCase = new UnityCliLoopServerInitializationUseCase(
                new ScriptedSecurityValidationService(ValidationResult.Failure("editor is not ready")),
                new UnityCliLoopServerStartupService(factory, repository));

            ServerInitializationResult<IUnityCliLoopServerInstance> result =
                GetCompletedResult(useCase.ExecuteAsync(CancellationToken.None));

            Assert.That(result.Success, Is.False);
            Assert.That(result.IsRunning, Is.False);
            Assert.That(result.Message, Is.EqualTo("editor is not ready"));
            Assert.That(result.ServerInstance, Is.Null);
            Assert.That(factory.CreatedServers, Is.Empty);
            Assert.That(repository.Calls, Is.Empty);
        }

        /// <summary>
        /// Verifies a valid editor state starts the created server, marks the session started, and returns that server as running.
        /// </summary>
        [Test]
        public void ExecuteAsync_WhenEditorStateIsValid_ReturnsRunningStartedServerAndMarksSession()
        {
            RecordingServerInstanceFactory factory = new RecordingServerInstanceFactory();
            RecordingSessionFlagsRepository repository = new RecordingSessionFlagsRepository();
            UnityCliLoopServerInitializationUseCase useCase = new UnityCliLoopServerInitializationUseCase(
                new ScriptedSecurityValidationService(ValidationResult.Success()),
                new UnityCliLoopServerStartupService(factory, repository));

            ServerInitializationResult<IUnityCliLoopServerInstance> result =
                GetCompletedResult(useCase.ExecuteAsync(CancellationToken.None));

            Assert.That(factory.CreatedServers.Count, Is.EqualTo(1));
            RecordingServerInstance createdServer = factory.CreatedServers[0];
            Assert.That(result.Success, Is.True);
            Assert.That(result.IsRunning, Is.True);
            Assert.That(result.Message, Is.EqualTo(ServerLifecycleMessages.InitializationSucceeded));
            Assert.That(result.ServerInstance, Is.SameAs(createdServer));
            Assert.That(createdServer.StartCallCount, Is.EqualTo(1));
            Assert.That(repository.Calls, Is.EqualTo(new[] { "MarkServerStarted" }));
        }

        private static T GetCompletedResult<T>(Task<T> task)
        {
            Assert.That(task.IsCompleted, Is.True, "Initialization must complete synchronously.");
            return task.GetAwaiter().GetResult();
        }

        /// <summary>
        /// Test support type that returns a scripted editor-state validation result.
        /// </summary>
        private sealed class ScriptedSecurityValidationService : ISecurityValidationService
        {
            private readonly ValidationResult _result;

            public ScriptedSecurityValidationService(ValidationResult result)
            {
                _result = result;
            }

            public ValidationResult ValidateEditorState()
            {
                return _result;
            }
        }

        /// <summary>
        /// Test support type that records the server instances it creates.
        /// </summary>
        private sealed class RecordingServerInstanceFactory : IUnityCliLoopServerInstanceFactory
        {
            public List<RecordingServerInstance> CreatedServers { get; } = new List<RecordingServerInstance>();

            public IUnityCliLoopServerInstance Create()
            {
                RecordingServerInstance server = new RecordingServerInstance();
                CreatedServers.Add(server);
                return server;
            }
        }

        /// <summary>
        /// Test support type that counts server lifecycle calls.
        /// </summary>
        private sealed class RecordingServerInstance : IUnityCliLoopServerInstance
        {
            public int StartCallCount { get; private set; }

            public bool IsRunning { get; private set; }

            public string Endpoint => "test";

            public void StartServer()
            {
                StartCallCount++;
                IsRunning = true;
            }

            public void StopServer()
            {
                IsRunning = false;
            }

            public void Dispose()
            {
                IsRunning = false;
            }
        }

        /// <summary>
        /// Test support type that records session flag mutations by name.
        /// </summary>
        private sealed class RecordingSessionFlagsRepository : ISessionFlagsRepository
        {
            public List<string> Calls { get; } = new List<string>();

            public bool GetIsServerRunning() => false;
            public bool GetIsServerManuallyStopped() => false;
            public bool GetIsAfterCompile() => false;
            public bool GetIsDomainReloadInProgress() => false;
            public bool GetShowReconnectingUI() => false;
            public void SetIsAfterCompile(bool isAfterCompile) => Calls.Add("SetIsAfterCompile");
            public void SetIsDomainReloadInProgress(bool isDomainReloadInProgress) => Calls.Add("SetIsDomainReloadInProgress");
            public void SetIsReconnecting(bool isReconnecting) => Calls.Add("SetIsReconnecting");
            public void SetShowReconnectingUI(bool showReconnectingUI) => Calls.Add("SetShowReconnectingUI");
            public void SetShowPostCompileReconnectingUI(bool showPostCompileReconnectingUI) => Calls.Add("SetShowPostCompileReconnectingUI");
            public void SetShouldAutoScanThirdPartyToolMigration(bool shouldAutoScanThirdPartyToolMigration) => Calls.Add("SetShouldAutoScanThirdPartyToolMigration");
            public bool ConsumeShouldAutoScanThirdPartyToolMigration() => false;
            public void MarkServerStarted() => Calls.Add("MarkServerStarted");
            public void MarkServerManuallyStopped() => Calls.Add("MarkServerManuallyStopped");
            public void ClearServerSession() => Calls.Add("ClearServerSession");
            public void ClearAfterCompileFlag() => Calls.Add("ClearAfterCompileFlag");
            public void ClearReconnectingFlags() => Calls.Add("ClearReconnectingFlags");
            public void ClearPostCompileReconnectingUI() => Calls.Add("ClearPostCompileReconnectingUI");
            public void ClearDomainReloadFlag() => Calls.Add("ClearDomainReloadFlag");
            public void ClearDomainReloadRecoveryFlags() => Calls.Add("ClearDomainReloadRecoveryFlags");
        }
    }
}
