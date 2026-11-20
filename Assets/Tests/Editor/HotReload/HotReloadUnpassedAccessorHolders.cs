namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    // No test passes this file to a run. The worker therefore binds these types through the
    // assembly's public surface, where each public property or indexer below lacks its internal
    // accessor, while the shim compile sees every accessor and the shim assembly cannot call it.

    /// <summary>
    /// A public property whose setter is internal.
    /// </summary>
    public sealed class HotReloadUnpassedInternalSetterHolder
    {
        public int Length { get; internal set; } = 3;

        public int ReadLength()
        {
            return Length;
        }
    }

    /// <summary>
    /// A public property whose getter is internal.
    /// </summary>
    public sealed class HotReloadUnpassedInternalGetterHolder
    {
        public int Value { internal get; set; } = 7;

        public int ReadValue()
        {
            return Value;
        }
    }

    /// <summary>
    /// A public property whose init accessor is internal.
    /// </summary>
    public sealed class HotReloadUnpassedInternalInitHolder
    {
        public int Length { get; internal init; } = 3;
    }

    /// <summary>
    /// A public indexer whose setter is internal.
    /// </summary>
    public sealed class HotReloadUnpassedIndexerInternalSetterHolder
    {
        private readonly int[] _items = { 11, 12, 13 };

        public int this[int index]
        {
            get => _items[index];
            internal set => _items[index] = value;
        }

        public int Peek(int index)
        {
            return _items[index];
        }
    }

    /// <summary>
    /// A public indexer whose getter is internal, with a public Length, so an Index argument still
    /// finds the implicit Index support.
    /// </summary>
    public sealed class HotReloadUnpassedIndexerInternalGetterHolder
    {
        private readonly int[] _items = { 21, 22, 23 };

        public int Length => _items.Length;

        public int this[int index]
        {
            internal get => _items[index];
            set => _items[index] = value;
        }
    }

    /// <summary>
    /// A public Length whose getter is internal, next to a public indexer and a public Slice, so
    /// only an Index or Range access that reads Length calls the internal accessor.
    /// </summary>
    public sealed class HotReloadUnpassedLengthInternalGetterHolder
    {
        private readonly int[] _items = { 31, 32, 33 };

        public int Length
        {
            internal get => _items.Length;
            set { }
        }

        public int this[int index] => _items[index];

        public int[] Slice(int start, int length)
        {
            int[] slice = new int[length];
            System.Array.Copy(_items, start, slice, 0, length);
            return slice;
        }
    }

    /// <summary>
    /// A generic type whose public indexer, keyed by the type parameter, has an internal setter,
    /// written through a constructed type.
    /// </summary>
    public sealed class HotReloadUnpassedGenericIndexerInternalSetterHolder<T>
    {
        private T _value;

        public T this[T key]
        {
            get => _value;
            internal set => _value = value;
        }
    }

    /// <summary>
    /// A public static property whose setter is internal.
    /// </summary>
    public static class HotReloadUnpassedStaticInternalSetterHolder
    {
        public static int Shared { get; internal set; }
    }
}
