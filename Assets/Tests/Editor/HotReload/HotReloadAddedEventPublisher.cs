using System;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Compiled publisher whose edited copy gains events, so a subscription to an event the
    /// compiled assembly lacks can be told apart from one to an event it already has.
    /// </summary>
    public sealed class HotReloadAddedEventPublisher
    {
        public event Action<int> Existing;

        public void RaiseExisting(int value)
        {
            Existing?.Invoke(value);
        }

        /// <summary>
        /// Nested publisher, because a nested type is looked up compiled by a name that differs
        /// from its source spelling.
        /// </summary>
        public sealed class Inner
        {
            public event Action<int> InnerExisting;

            public void RaiseInnerExisting(int value)
            {
                InnerExisting?.Invoke(value);
            }
        }
    }
}
