using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEditor.Compilation;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.Infrastructure;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies CompileController drives the compile request through its pipeline port:
    /// event registration, completion, the wait-for-existing-task path, and cleanup.
    /// </summary>
    [TestFixture]
    public sealed class CompileControllerPipelineTests
    {
        private UnityCliLoopEditorSessionStateSnapshot _originalSnapshot;

        [SetUp]
        public void SetUp()
        {
            _originalSnapshot = UnityCliLoopEditorSessionStateTestFactory.CaptureSnapshot();
        }

        [TearDown]
        public void TearDown()
        {
            CompileApiUpdaterConsentState.EndCliCompile();
            _originalSnapshot.Restore();
        }

        /// <summary>
        /// What: a normal compile refreshes assets, registers both callbacks, requests a non-clean
        /// compile, starts the watchdog, and completes with the messages the assembly callback reported.
        /// </summary>
        [Test]
        public async Task TryCompileAsync_WhenPipelineFinishes_ReturnsReportedMessagesAndClearsState()
        {
            FakeCompilePipelinePort pipeline = new()
            {
                CompleteOnRequest = true,
                ReportedMessages = new[] { CreateMessage(CompilerMessageType.Warning, "sample warning") }
            };
            using CompileController controller = CreateController(pipeline);
            List<string> startedMessages = new();
            List<string> compiledAssemblies = new();
            controller.OnCompileStarted += startedMessages.Add;
            controller.OnAssemblyCompiled += (assemblyName, _) => compiledAssemblies.Add(assemblyName);

            CompileResult result = await controller.TryCompileAsync(
                forceRecompile: false,
                playModeStopWarning: null,
                CancellationToken.None);

            Assert.That(result.Success, Is.True);
            Assert.That(result.WarningCount, Is.EqualTo(1));
            Assert.That(pipeline.RefreshCount, Is.EqualTo(1));
            Assert.That(pipeline.SubscribeCount, Is.EqualTo(1));
            Assert.That(pipeline.UnsubscribeCount, Is.EqualTo(1));
            Assert.That(pipeline.RequestedCleanBuildCache, Is.EqualTo(new[] { false }));
            Assert.That(pipeline.WatchdogStartCount, Is.EqualTo(1));
            Assert.That(startedMessages, Is.EqualTo(new[] { "Compilation started after asset refresh..." }));
            Assert.That(compiledAssemblies, Is.EqualTo(new[] { "Sample.dll" }));
            Assert.That(controller.IsCompiling, Is.False);
            Assert.That(CompileApiUpdaterConsentState.IsCliCompileInFlight, Is.False);
        }

        /// <summary>
        /// What: a forced recompile requests a clean build cache and returns an indeterminate result.
        /// </summary>
        [Test]
        public async Task TryCompileAsync_WhenForced_RequestsCleanBuildCacheAndReturnsIndeterminateResult()
        {
            FakeCompilePipelinePort pipeline = new() { CompleteOnRequest = true };
            using CompileController controller = CreateController(pipeline);
            List<string> startedMessages = new();
            controller.OnCompileStarted += startedMessages.Add;

            CompileResult result = await controller.TryCompileAsync(
                forceRecompile: true,
                playModeStopWarning: null,
                CancellationToken.None);

            Assert.That(result.IsIndeterminate, Is.True);
            Assert.That(pipeline.RequestedCleanBuildCache, Is.EqualTo(new[] { true }));
            Assert.That(startedMessages, Is.EqualTo(new[] { "Forced recompile started after asset refresh..." }));
        }

        /// <summary>
        /// What: Assembly Definition errors found after the refresh stop the compile before any callback
        /// is registered or compile is requested, and the controller returns to idle.
        /// </summary>
        [Test]
        public async Task TryCompileAsync_WhenAssemblyDefinitionErrorsExist_ReturnsFailureWithoutRequestingCompile()
        {
            // Why CompleteOnRequest: if the early return regresses, the request still finishes
            // and the assertions fail instead of the test hanging on a compile that never ends.
            FakeCompilePipelinePort pipeline = new()
            {
                CompleteOnRequest = true,
                AssemblyDefinitionErrors = new AssemblyDefinitionConsoleErrorResult(new[]
                {
                    new AssemblyDefinitionConsoleError("broken asmdef", "Assets/Broken.asmdef", 1)
                })
            };
            using CompileController controller = CreateController(pipeline);

            CompileResult result = await controller.TryCompileAsync(
                forceRecompile: false,
                playModeStopWarning: null,
                CancellationToken.None);

            Assert.That(result.Success, Is.False);
            Assert.That(pipeline.RefreshCount, Is.EqualTo(1));
            Assert.That(pipeline.SubscribeCount, Is.EqualTo(0));
            Assert.That(pipeline.RequestedCleanBuildCache, Is.Empty);
            Assert.That(pipeline.WatchdogStartCount, Is.EqualTo(0));
            Assert.That(controller.IsCompiling, Is.False);
            Assert.That(CompileApiUpdaterConsentState.IsCliCompileInFlight, Is.False);
        }

        /// <summary>
        /// What: a second TryCompileAsync during an in-flight compile waits for the existing request
        /// instead of starting another one, and both callers receive the same result.
        /// </summary>
        [Test]
        public async Task TryCompileAsync_WhenAlreadyCompiling_WaitsForExistingRequest()
        {
            FakeCompilePipelinePort pipeline = new();
            using CompileController controller = CreateController(pipeline);

            Task<CompileResult> firstCompile = controller.TryCompileAsync(
                forceRecompile: false,
                playModeStopWarning: null,
                CancellationToken.None);
            Task<CompileResult> secondCompile = controller.TryCompileAsync(
                forceRecompile: true,
                playModeStopWarning: null,
                CancellationToken.None);

            // Why before completing: if the second call started its own compile, the first request
            // would never finish, so the check must fail before awaiting it.
            Assert.That(pipeline.RefreshCount, Is.EqualTo(1));
            Assert.That(pipeline.RequestedCleanBuildCache, Is.EqualTo(new[] { false }));
            Assert.That(controller.IsCompiling, Is.True);
            Assert.That(secondCompile.IsCompleted, Is.False);
            pipeline.CompilationFinished(null);
            CompileResult firstResult = await firstCompile;
            CompileResult secondResult = await secondCompile;

            Assert.That(secondResult, Is.SameAs(firstResult));
        }

        /// <summary>
        /// What: when requesting compile throws after callbacks were registered, the controller
        /// unregisters them, clears the in-flight state, and lets the exception reach the caller.
        /// </summary>
        [Test]
        public async Task TryCompileAsync_WhenRequestThrows_UnregistersCallbacksAndClearsState()
        {
            FakeCompilePipelinePort pipeline = new()
            {
                RequestException = new InvalidOperationException("request failed")
            };
            using CompileController controller = CreateController(pipeline);

            // Why not Assert.ThrowsAsync: it blocks the main thread synchronously in this NUnit version.
            try
            {
                await controller.TryCompileAsync(
                    forceRecompile: false,
                    playModeStopWarning: null,
                    CancellationToken.None);
                Assert.Fail("TryCompileAsync should rethrow the request failure.");
            }
            catch (InvalidOperationException e)
            {
                Assert.That(e.Message, Is.EqualTo("request failed"));
            }

            Assert.That(pipeline.UnsubscribeCount, Is.EqualTo(1));
            Assert.That(pipeline.WatchdogStartCount, Is.EqualTo(0));
            Assert.That(controller.IsCompiling, Is.False);
            Assert.That(CompileApiUpdaterConsentState.IsCliCompileInFlight, Is.False);
        }

        /// <summary>
        /// What: Cleanup unregisters the callbacks through the port and cancels a pending compile request.
        /// </summary>
        [Test]
        public async Task Cleanup_WhenCompileIsPending_UnregistersCallbacksAndCancelsRequest()
        {
            FakeCompilePipelinePort pipeline = new();
            using CompileController controller = CreateController(pipeline);
            Task<CompileResult> pendingCompile = controller.TryCompileAsync(
                forceRecompile: false,
                playModeStopWarning: null,
                CancellationToken.None);

            controller.Cleanup();

            Assert.That(pipeline.UnsubscribeCount, Is.EqualTo(1));
            Assert.That(controller.IsCompiling, Is.False);
            Assert.That(CompileApiUpdaterConsentState.IsCliCompileInFlight, Is.False);
            try
            {
                await pendingCompile;
                Assert.Fail("The pending compile should be canceled by Cleanup.");
            }
            catch (TaskCanceledException)
            {
            }
        }

        private static CompileController CreateController(FakeCompilePipelinePort pipeline)
        {
            CompileController controller = new(
                UnityCliLoopEditorSessionStateTestFactory.CreateCompileResultSessionRepository(),
                UnityCliLoopEditorSessionStateTestFactory.CreatePendingCompileSessionRepository());
            // Why: the real resolver subscribes to Scene and Play Mode events and reads the open Scenes.
            controller.SetExternalSceneChangeResolutionForTesting(_ => (true, null, Array.Empty<string>()));
            controller.SetCompilePipelineForTesting(pipeline);
            return controller;
        }

        private static CompilerMessage CreateMessage(CompilerMessageType type, string message)
        {
            return new CompilerMessage
            {
                type = type,
                message = message,
                file = "Assets/Sample.cs",
                line = 1,
                column = 1
            };
        }

        /// <summary>
        /// Records pipeline calls and optionally finishes the compile synchronously on request.
        /// </summary>
        private sealed class FakeCompilePipelinePort : ICompilePipelinePort
        {
            private Action<object> _compilationFinished;
            private Action<string, CompilerMessage[]> _assemblyFinished;

            public bool CompleteOnRequest { get; set; }
            public CompilerMessage[] ReportedMessages { get; set; } = Array.Empty<CompilerMessage>();
            public AssemblyDefinitionConsoleErrorResult AssemblyDefinitionErrors { get; set; } =
                new(Array.Empty<AssemblyDefinitionConsoleError>());
            public Exception RequestException { get; set; }
            public int RefreshCount { get; private set; }
            public int SubscribeCount { get; private set; }
            public int UnsubscribeCount { get; private set; }
            public int WatchdogStartCount { get; private set; }
            public List<bool> RequestedCleanBuildCache { get; } = new();

            public void RefreshAssets()
            {
                RefreshCount++;
            }

            public AssemblyDefinitionConsoleErrorResult FindCurrentAssemblyDefinitionErrors()
            {
                return AssemblyDefinitionErrors;
            }

            public UnityCliLoopConsoleLogEntry[] ReadConsoleErrorEntries()
            {
                return Array.Empty<UnityCliLoopConsoleLogEntry>();
            }

            public void SubscribeCompilationEvents(
                Action<object> compilationFinished,
                Action<string, CompilerMessage[]> assemblyFinished)
            {
                SubscribeCount++;
                _compilationFinished = compilationFinished;
                _assemblyFinished = assemblyFinished;
            }

            public void UnsubscribeCompilationEvents(
                Action<object> compilationFinished,
                Action<string, CompilerMessage[]> assemblyFinished)
            {
                UnsubscribeCount++;
            }

            public void RequestScriptCompilation(bool cleanBuildCache)
            {
                RequestedCleanBuildCache.Add(cleanBuildCache);
                if (RequestException != null)
                {
                    throw RequestException;
                }

                if (!CompleteOnRequest)
                {
                    return;
                }

                _assemblyFinished("Library/ScriptAssemblies/Sample.dll", ReportedMessages);
                _compilationFinished(null);
            }

            public void StartWatchdog(
                CompileLifecycleRecoveryCoordinator coordinator,
                TaskCompletionSource<CompileResult> compileTask,
                CancellationToken ct)
            {
                WatchdogStartCount++;
            }

            public void CompilationFinished(object context)
            {
                _compilationFinished(context);
            }
        }
    }
}
