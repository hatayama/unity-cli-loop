using System.Collections;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    // No test passes this file to a run. The worker therefore binds these types through the
    // assembly's public surface, which includes protected members. When this assembly is the one
    // being edited, the shim compile sees a copy with every member public, and each type below makes
    // those two disagree. When another assembly is edited, the copy keeps hidden members hidden.

    /// <summary>
    /// A constructor the worker binds, since protected is on the public surface, but the shim
    /// assembly cannot call.
    /// </summary>
    public class HotReloadUnpassedProtectedConstructor
    {
        protected HotReloadUnpassedProtectedConstructor(int value)
        {
        }
    }

    /// <summary>
    /// A public overload the worker binds, and a better internal one only the shim compile sees.
    /// </summary>
    public sealed class HotReloadUnpassedHiddenOverload
    {
        public int Chosen;

        public HotReloadUnpassedHiddenOverload(long value)
        {
            Chosen = 1;
        }

        internal HotReloadUnpassedHiddenOverload(int value)
        {
            Chosen = 2;
        }
    }

    /// <summary>
    /// A collection whose only Add the worker's binding cannot see at all.
    /// </summary>
    public sealed class HotReloadUnpassedInternalAdd : IEnumerable
    {
        internal void Add(int value)
        {
        }

        public IEnumerator GetEnumerator()
        {
            return new int[0].GetEnumerator();
        }
    }

    /// <summary>
    /// A nested type on the public surface of this assembly that the shim assembly cannot name.
    /// </summary>
    public class HotReloadUnpassedOuter
    {
        protected sealed class ProtectedNested
        {
        }
    }

    /// <summary>
    /// A collection whose Add is on the public surface of this assembly but cannot be called from
    /// the shim assembly.
    /// </summary>
    public sealed class HotReloadUnpassedProtectedAdd : IEnumerable
    {
        protected void Add(int value)
        {
        }

        public IEnumerator GetEnumerator()
        {
            return new int[0].GetEnumerator();
        }
    }

    /// <summary>
    /// The collection form of <see cref="HotReloadUnpassedHiddenOverload"/>.
    /// </summary>
    public sealed class HotReloadUnpassedHiddenAddOverload : IEnumerable
    {
        public int Chosen;

        public void Add(long value)
        {
            Chosen = 1;
        }

        internal void Add(int value)
        {
            Chosen = 2;
        }

        public IEnumerator GetEnumerator()
        {
            return new int[0].GetEnumerator();
        }
    }
}
