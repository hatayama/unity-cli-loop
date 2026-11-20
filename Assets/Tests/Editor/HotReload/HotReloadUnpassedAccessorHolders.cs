using System;

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

    /// <summary>
    /// A record whose public property has an internal init accessor, set through a with expression.
    /// </summary>
    public sealed record HotReloadUnpassedInternalInitRecord
    {
        public int Length { get; internal init; } = 3;
    }

    /// <summary>
    /// A public property whose getter is internal and whose value is filled by a nested object
    /// initializer, which reads the property rather than setting it.
    /// </summary>
    public sealed class HotReloadUnpassedNestedInitializerOuter
    {
        public HotReloadUnpassedNestedInitializerInner Inner { internal get; set; } = new HotReloadUnpassedNestedInitializerInner();

        public int ReadInner()
        {
            return Inner.Count;
        }
    }

    /// <summary>
    /// The object a nested object initializer fills.
    /// </summary>
    public sealed class HotReloadUnpassedNestedInitializerInner
    {
        public int Count;
    }

    /// <summary>
    /// A base type whose public property has an internal setter, which a type in the edited file
    /// writes by its bare name.
    /// </summary>
    public class HotReloadUnpassedAccessorBase
    {
        public int Length { get; internal set; } = 3;

        public static int Shared { get; internal set; }

        public int ReadLength()
        {
            return Length;
        }
    }

    /// <summary>
    /// A public property with no setter at all, whose write is a genuine error.
    /// </summary>
    public sealed class HotReloadUnpassedGetOnlyHolder
    {
        public int Fixed { get; } = 3;
    }

    /// <summary>
    /// An indexer whose getter is internal, indexed with an argument of the wrong type, whose read
    /// is a genuine error rather than a use of the internal getter.
    /// </summary>
    public sealed class HotReloadUnpassedIntIndexerInternalGetterHolder
    {
        private readonly int[] _items = { 61, 62, 63 };

        public int this[int index]
        {
            internal get => _items[index];
            set => _items[index] = value;
        }
    }

    /// <summary>
    /// A read-only int indexer beside a string indexer whose getter is internal, so a write through
    /// the int indexer is a genuine error that the string indexer must not explain.
    /// </summary>
    public sealed class HotReloadUnpassedOverloadedIndexerHolder
    {
        public int this[int index] => index;

        public int this[string key]
        {
            internal get => key.Length;
            set { }
        }
    }

    /// <summary>
    /// A type whose only indexer takes a string and has an internal getter, next to a public
    /// Length, so an Index argument has no indexer to go through and is a genuine error.
    /// </summary>
    public sealed class HotReloadUnpassedStringIndexerInternalGetterHolder
    {
        public int Length => 3;

        public int this[string key]
        {
            internal get => key.Length;
            set { }
        }
    }

    /// <summary>
    /// A public Length and Slice beside an internal indexer that takes a Range. On the public
    /// surface a range binds through Slice, while the compiler calls the internal indexer.
    /// </summary>
    public sealed class HotReloadUnpassedInternalRangeIndexerHolder
    {
        private readonly int[] _items = { 1, 2, 3, 4 };

        public int Length => _items.Length;

        public int[] Slice(int start, int length)
        {
            return new[] { start, length };
        }

        internal int[] this[Range range] => _items[range];
    }

    /// <summary>
    /// A public Length and int indexer beside an internal indexer that takes an Index. On the
    /// public surface an Index binds through Length and the int indexer, while the compiler calls
    /// the internal indexer.
    /// </summary>
    public sealed class HotReloadUnpassedInternalIndexIndexerHolder
    {
        private readonly int[] _items = { 1, 2, 3, 4 };

        public int Length => _items.Length;

        public int this[int index]
        {
            get => _items[index];
            set => _items[index] = value;
        }

        internal int this[Index index]
        {
            get => _items[index] * 10;
            set => _items[index] = value * 10;
        }
    }

    /// <summary>
    /// A base type with a public Length and Slice beside an internal indexer that takes a Range,
    /// reached through a derived type and through a type parameter constrained to it.
    /// </summary>
    public class HotReloadUnpassedInternalRangeIndexerBase
    {
        private readonly int[] _items = { 1, 2, 3, 4 };

        public int Length => _items.Length;

        public int[] Slice(int start, int length)
        {
            return new[] { start, length };
        }

        internal int[] this[Range range] => _items[range];
    }

    /// <summary>
    /// A derived type with no indexer of its own, so a Range access finds the internal indexer of
    /// its base.
    /// </summary>
    public sealed class HotReloadUnpassedInternalRangeIndexerDerived : HotReloadUnpassedInternalRangeIndexerBase
    {
    }

    /// <summary>
    /// A generic type whose internal indexer takes the type parameter, beside a public Length and
    /// int indexer, so an Index access through the type constructed with Index calls that indexer.
    /// </summary>
    public sealed class HotReloadUnpassedGenericKeyIndexerHolder<T>
    {
        public int Length => 4;

        public int this[int index] => index;

        internal int this[T key] => 99;
    }

    /// <summary>
    /// An internal indexer that takes object, beside a public Length and int indexer. An Index
    /// argument converts to object, so the compiler calls that indexer.
    /// </summary>
    public sealed class HotReloadUnpassedObjectKeyIndexerHolder
    {
        public int Length => 4;

        public int this[int index] => index;

        internal int this[object key] => 99;
    }

    /// <summary>
    /// A generic type whose internal indexer takes an array of the type parameter, beside a public
    /// Length and int indexer. An Index never converts to an array, so an Index access calls only
    /// the public members.
    /// </summary>
    public sealed class HotReloadUnpassedGenericArrayKeyIndexerHolder<T>
    {
        public int Length => 4;

        public int this[int index] => index;

        internal int this[T[] keys] => 99;
    }

    /// <summary>
    /// A key that an Index converts to through its own implicit conversion.
    /// </summary>
    public readonly struct HotReloadUnpassedIndexKey
    {
        public HotReloadUnpassedIndexKey(Index index)
        {
            Index = index;
        }

        public Index Index { get; }

        public static implicit operator HotReloadUnpassedIndexKey(Index index)
        {
            return new HotReloadUnpassedIndexKey(index);
        }
    }

    /// <summary>
    /// An internal indexer that takes a key an Index converts to, beside a public Length and int
    /// indexer, so the compiler calls that indexer for an Index access.
    /// </summary>
    public sealed class HotReloadUnpassedConvertedKeyIndexerHolder
    {
        public int Length => 4;

        public int this[int index] => index;

        internal int this[HotReloadUnpassedIndexKey key] => 99;
    }

    /// <summary>
    /// A base key with an implicit conversion from Index, which yields the base key itself.
    /// </summary>
    public class HotReloadUnpassedBaseKey
    {
        public static implicit operator HotReloadUnpassedBaseKey(Index index)
        {
            return new HotReloadUnpassedBaseKey();
        }
    }

    /// <summary>
    /// A derived key with no conversion of its own. The base key's conversion yields a base key,
    /// which converts to it only explicitly, so an Index does not convert to it.
    /// </summary>
    public sealed class HotReloadUnpassedDerivedKey : HotReloadUnpassedBaseKey
    {
    }

    /// <summary>
    /// An internal indexer that takes the derived key, beside a public Length and int indexer, so an
    /// Index access calls only the public members.
    /// </summary>
    public sealed class HotReloadUnpassedDerivedKeyIndexerHolder
    {
        public int Length => 4;

        public int this[int index] => index;

        internal int this[HotReloadUnpassedDerivedKey key] => 99;
    }

    /// <summary>
    /// A generic key that converts implicitly from its type argument.
    /// </summary>
    public readonly struct HotReloadUnpassedGenericKey<T>
    {
        public HotReloadUnpassedGenericKey(T value)
        {
            Value = value;
        }

        public T Value { get; }

        public static implicit operator HotReloadUnpassedGenericKey<T>(T value)
        {
            return new HotReloadUnpassedGenericKey<T>(value);
        }
    }

    /// <summary>
    /// An internal indexer that takes the generic key of the type parameter, beside a public Length
    /// and int indexer. Constructed with Index, an Index converts to that key, so the compiler calls
    /// that indexer.
    /// </summary>
    public sealed class HotReloadUnpassedGenericConvertedKeyIndexerHolder<T>
    {
        public int Length => 4;

        public int this[int index] => index;

        internal int this[HotReloadUnpassedGenericKey<T> key] => 99;
    }
}
