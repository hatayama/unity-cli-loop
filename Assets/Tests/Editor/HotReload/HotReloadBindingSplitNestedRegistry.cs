using System;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Holds a compiled API as a nested type, so a skipped row that names the type declaring a
    /// compiled signature has to find the file of a nested type.
    /// </summary>
    public static class HotReloadBindingSplitNestedRegistry
    {
        /// <summary>
        /// A compiled API that takes a handler of the payload type, like the top-level registry.
        /// </summary>
        public sealed class Inner
        {
            private Action<HotReloadBindingSplitPayload> _handler;

            public void Register(Action<HotReloadBindingSplitPayload> handler)
            {
                _handler = handler;
            }

            public void Raise(int value)
            {
                _handler?.Invoke(new HotReloadBindingSplitPayload { Value = value });
            }
        }
    }
}
