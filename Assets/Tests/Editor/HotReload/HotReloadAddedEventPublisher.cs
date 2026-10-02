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

        // Compiled field an edited copy turns into an event of the same name.
        public int Clash;

        public int Count => 0;

        public HotReloadAddedEventPublisher Self()
        {
            return this;
        }

        // Private, so a body calling it goes through accessor delegates.
        private int Secret()
        {
            return 2;
        }

        public void RaiseExisting(int value)
        {
            Existing?.Invoke(value);
        }

        /// <summary>
        /// Nested struct, because an event added to a struct cannot live in the added-field store.
        /// </summary>
        public struct Payload
        {
            public int Value;
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

    /// <summary>
    /// Generic publisher, because the added-field store keys an event by the open definition, so
    /// every closed instantiation would share one slot.
    /// </summary>
    public sealed class HotReloadAddedEventGenericHost<T>
    {
        public static int Marker;
    }

    /// <summary>
    /// A delegate type that code in another assembly cannot name, so an event of this type
    /// cannot be reached from a shim.
    /// </summary>
    internal delegate void HotReloadAddedEventHiddenHandler(int value);
}
