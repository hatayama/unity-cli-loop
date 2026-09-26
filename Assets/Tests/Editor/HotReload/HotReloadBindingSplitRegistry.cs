using System;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// A compiled API that takes a handler of the payload type, so an added method that registers
    /// a lambda binds against the compiled payload rather than one a reload declares from source.
    /// </summary>
    public sealed class HotReloadBindingSplitRegistry
    {
        private Action<HotReloadBindingSplitPayload> _handler;

        public void Register(Action<HotReloadBindingSplitPayload> handler)
        {
            _handler = handler;
        }

        public HotReloadBindingSplitPayload this[int value] => new HotReloadBindingSplitPayload { Value = value };

        public void Raise(int value)
        {
            _handler?.Invoke(new HotReloadBindingSplitPayload { Value = value });
        }
    }
}
