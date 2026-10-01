using System.Runtime.CompilerServices;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Compiled subscriber whose edited copy subscribes to an event the publisher's edited copy
    /// adds, so a test can tell whether a raise reaches it.
    /// </summary>
    public sealed class HotReloadAddedEventApplySubscriber
    {
        private int _received;

        public int Received => _received;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public void Wire(HotReloadAddedEventApplyPublisher publisher)
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public void Unwire(HotReloadAddedEventApplyPublisher publisher)
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public void WireStatic()
        {
        }

        public void Accept(int value)
        {
            _received += value;
        }
    }
}
