using System;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Substitutes the hot-reload side of the coordination point for the life of the scope and
    /// puts the previously installed port back on dispose. Members the test does not override on
    /// <see cref="Port"/> still answer from that previous port.
    /// </summary>
    public sealed class HotReloadSidePortScope : IDisposable
    {
        private readonly IHotReloadPausePointPort _previous;

        public HotReloadSidePortScope()
        {
            _previous = HotReloadPausePointCoordination.HotReloadSide;
            Port = new StubHotReloadPausePointPort { Inner = _previous };
            HotReloadPausePointCoordination.HotReloadSide = Port;
        }

        /// <summary>The stub the test configures. Overriding a member replaces just that answer.</summary>
        public StubHotReloadPausePointPort Port { get; }

        public void Dispose()
        {
            HotReloadPausePointCoordination.HotReloadSide = _previous;
        }
    }

    /// <summary>
    /// Substitutes the pause-point side of the coordination point for the life of the scope and
    /// puts the previously installed port back on dispose.
    /// </summary>
    public sealed class PausePointSidePortScope : IDisposable
    {
        private readonly IPausePointHotReloadPort _previous;

        public PausePointSidePortScope()
        {
            _previous = HotReloadPausePointCoordination.PausePointSide;
            Port = new StubPausePointHotReloadPort { Inner = _previous };
            HotReloadPausePointCoordination.PausePointSide = Port;
        }

        /// <summary>The stub the test configures. Overriding a member replaces just that answer.</summary>
        public StubPausePointHotReloadPort Port { get; }

        public void Dispose()
        {
            HotReloadPausePointCoordination.PausePointSide = _previous;
        }
    }
}
