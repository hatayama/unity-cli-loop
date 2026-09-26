using System.Runtime.CompilerServices;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Compiled subscriber whose edited copy subscribes to events of HotReloadAddedEventPublisher.
    /// </summary>
    public sealed class HotReloadAddedEventSubscriber
    {
        private int _received;

        public int Received => _received;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public void Wire(HotReloadAddedEventPublisher publisher)
        {
            publisher.Existing += Accept;
        }

        public void Accept(int value)
        {
            _received += value;
        }

        public void Tick()
        {
            _received++;
        }

        private void OnValue(int value)
        {
            _received += value;
        }
    }
}
