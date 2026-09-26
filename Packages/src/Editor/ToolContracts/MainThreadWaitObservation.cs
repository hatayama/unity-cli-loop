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
    /// Holds the wait observer of each async flow. An AsyncLocal keeps one value per flow, so a
    /// request that sets its observer never sees the observer of another request.
    /// </summary>
    internal sealed class MainThreadWaitObservationService
    {
        private readonly AsyncLocal<IMainThreadWaitObserver> _currentObserver =
            new AsyncLocal<IMainThreadWaitObserver>();

        internal IMainThreadWaitObserver Current => _currentObserver.Value;

        internal void SetCurrent(IMainThreadWaitObserver observer)
        {
            _currentObserver.Value = observer;
        }
    }

    /// <summary>
    /// Lets a tool request learn when it is only waiting for the Editor main thread, without tools
    /// having to report it themselves: the request sets the observer of its async flow, and the
    /// main-thread switch reports each wait to it.
    /// </summary>
    internal static class MainThreadWaitObservation
    {
        private static readonly MainThreadWaitObservationService ServiceValue = new MainThreadWaitObservationService();

        internal static IMainThreadWaitObserver Current => ServiceValue.Current;

        internal static void SetCurrent(IMainThreadWaitObserver observer)
        {
            ServiceValue.SetCurrent(observer);
        }
    }
}
