using System.Collections;
using System.Runtime.CompilerServices;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// A compiled host the object-creation tests add fields and properties to, with a nested type
    /// that can be named from a shim only through its outer type.
    /// </summary>
    public class HotReloadObjectCreationHost
    {
        public sealed class Nested
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int Read()
        {
            return -1;
        }
    }

    /// <summary>
    /// A public constructor on a type the shim assembly cannot name.
    /// </summary>
    internal sealed class HotReloadObjectCreationInternalType
    {
        public HotReloadObjectCreationInternalType()
        {
        }
    }

    /// <summary>
    /// A public type whose constructors differ in visibility, and which a reload can add to.
    /// </summary>
    public sealed class HotReloadObjectCreationRestrictedType
    {
        public int Number;

        public HotReloadObjectCreationRestrictedType()
        {
        }

        internal HotReloadObjectCreationRestrictedType(int number)
        {
            Number = number;
        }
    }

    /// <summary>
    /// A public nested type that a type argument on its outer type can make unnameable.
    /// </summary>
    public sealed class HotReloadObjectCreationGenericOuter<T>
    {
        public sealed class Inner
        {
        }
    }

    /// <summary>
    /// A public collection whose Add the shim assembly cannot call, which a collection initializer
    /// binds without naming it.
    /// </summary>
    public sealed class HotReloadObjectCreationInternalAdd : IEnumerable
    {
        internal void Add(int value)
        {
        }

        internal void Add(System.Text.StringBuilder value)
        {
        }

        public IEnumerator GetEnumerator()
        {
            return new int[0].GetEnumerator();
        }
    }
}
