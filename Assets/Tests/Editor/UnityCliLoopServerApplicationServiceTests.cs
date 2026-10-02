using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Application;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies the server application service exposes controller state and forwards control requests.
    /// </summary>
    public sealed class UnityCliLoopServerApplicationServiceTests
    {
        /// <summary>
        /// Verifies the service reports the controller's running state and recovery task.
        /// </summary>
        [Test]
        public void StateProperties_WhenControllerIsRunning_ReturnControllerStateAndRecoveryTask()
        {
            RecordingServerController controller = new RecordingServerController();
            controller.IsServerRunning = true;
            UnityCliLoopServerApplicationService service = new UnityCliLoopServerApplicationService(controller);

            Assert.That(service.IsServerRunning, Is.True);
            Assert.That(service.RecoveryTask, Is.SameAs(controller.RecoveryTask));
        }

        /// <summary>
        /// Verifies start and stop requests reach the controller in the order they were made.
        /// </summary>
        [Test]
        public void StartServerAndStopServer_WhenCalled_ForwardRequestsInOrder()
        {
            RecordingServerController controller = new RecordingServerController();
            UnityCliLoopServerApplicationService service = new UnityCliLoopServerApplicationService(controller);

            service.StartServer();
            service.StopServer();

            Assert.That(controller.Calls, Is.EqualTo(new[] { "Start", "Stop" }));
        }

        /// <summary>
        /// Verifies state-changed handlers are added to and removed from the controller as the same delegate.
        /// </summary>
        [Test]
        public void ServerStateChangedHandlers_WhenAddedAndRemoved_ForwardSameHandlerToController()
        {
            RecordingServerController controller = new RecordingServerController();
            UnityCliLoopServerApplicationService service = new UnityCliLoopServerApplicationService(controller);
            Action handler = () => { };

            service.AddServerStateChangedHandler(handler);
            service.RemoveServerStateChangedHandler(handler);

            Assert.That(controller.Calls, Is.EqualTo(new[] { "AddHandler", "RemoveHandler" }));
            Assert.That(controller.AddedHandler, Is.SameAs(handler));
            Assert.That(controller.RemovedHandler, Is.SameAs(handler));
        }

        /// <summary>
        /// Test support type that records server controller calls.
        /// </summary>
        private sealed class RecordingServerController : IUnityCliLoopServerController
        {
            public List<string> Calls { get; } = new List<string>();
            public Action AddedHandler { get; private set; }
            public Action RemovedHandler { get; private set; }

            public bool IsServerRunning { get; set; }

            public Task RecoveryTask { get; } = Task.FromResult(1);

            public void StartServer()
            {
                Calls.Add("Start");
            }

            public void StopServer()
            {
                Calls.Add("Stop");
            }

            public void AddServerStateChangedHandler(Action handler)
            {
                Calls.Add("AddHandler");
                AddedHandler = handler;
            }

            public void RemoveServerStateChangedHandler(Action handler)
            {
                Calls.Add("RemoveHandler");
                RemovedHandler = handler;
            }
        }
    }
}
