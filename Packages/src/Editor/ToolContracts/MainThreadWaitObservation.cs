using System.Threading;

namespace io.github.hatayama.UnityCliLoop.ToolContracts
{
    /// <summary>
    /// Receives the start and end of each switch that has to wait in the Editor main-thread queue.
    /// Implementations run inside the queue's resume path, so they must only update counters and
    /// never throw: a throwing observer would leave the awaiting method suspended forever.
    /// </summary>
    internal interface IMainThreadWaitObserver
    {
        void OnWaitStarted();
        void OnWaitEnded();
    }

    /// <summary>
    /// Holds the wait observer of the current async flow, so a tool request can learn when it is
    /// only waiting for the Editor main thread without tools having to report it themselves.
    /// </summary>
    /// <remarks>
    /// Why a static field and not an instance service like the main-thread dispatcher: an
    /// AsyncLocal keeps one value per async flow, so the field shares nothing between requests.
    /// Each request sets its own observer, and the switch awaiter, a struct any caller can create,
    /// reads it without a reference to that request.
    /// </remarks>
    internal static class MainThreadWaitObservation
    {
        private static readonly AsyncLocal<IMainThreadWaitObserver> CurrentObserver = new();

        internal static IMainThreadWaitObserver Current => CurrentObserver.Value;

        internal static void SetCurrent(IMainThreadWaitObserver observer)
        {
            CurrentObserver.Value = observer;
        }
    }
}
