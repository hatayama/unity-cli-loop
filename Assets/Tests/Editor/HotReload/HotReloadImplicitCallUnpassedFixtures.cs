using System;
using System.Collections;
using System.Threading.Tasks;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Internal extension methods the compiler calls for await, foreach, deconstruction, collection
    /// initializers, and query expressions, declared in a file the implicit-call layout tests do not
    /// pass to the run, so the transform worker sees this assembly through its public surface only.
    /// </summary>
    public static class HotReloadUnpassedImplicitCallExtensions
    {
        internal static HotReloadImplicitAwaiter GetAwaiter(this HotReloadUnpassedAwaitable awaitable)
        {
            return new HotReloadImplicitAwaiter(awaitable.Value);
        }

        internal static HotReloadImplicitEnumerator GetEnumerator(this HotReloadUnpassedRange range)
        {
            return new HotReloadImplicitEnumerator(range.Count);
        }

        internal static void Deconstruct(this HotReloadUnpassedExtensionPair pair, out int first, out int second)
        {
            first = pair.First;
            second = pair.Second;
        }

        internal static void Add(this HotReloadUnpassedBag bag, int value)
        {
            bag.Sum += value;
        }

        internal static HotReloadUnpassedSource Select(this HotReloadUnpassedSource source, Func<int, int> selector)
        {
            return new HotReloadUnpassedSource { Value = selector(source.Value) };
        }

        internal static HotReloadUnpassedFilterSource Where(
            this HotReloadUnpassedFilterSource source,
            Func<int, bool> predicate)
        {
            return new HotReloadUnpassedFilterSource { Value = predicate(source.Value) ? source.Value : 0 };
        }
    }

    /// <summary>
    /// Awaitable whose awaiter comes from an internal extension method in an unpassed file.
    /// </summary>
    public sealed class HotReloadUnpassedAwaitable
    {
        public int Value = 11;
    }

    /// <summary>
    /// Range enumerated through an internal extension GetEnumerator in an unpassed file.
    /// </summary>
    public sealed class HotReloadUnpassedRange
    {
        public int Count = 3;
    }

    /// <summary>
    /// Pair deconstructed through an internal extension Deconstruct in an unpassed file.
    /// </summary>
    public sealed class HotReloadUnpassedExtensionPair
    {
        public int First = 21;
        public int Second = 31;
    }

    /// <summary>
    /// Pair with an internal instance Deconstruct, declared in an unpassed file.
    /// </summary>
    public sealed class HotReloadUnpassedInternalPair
    {
        public int First = 41;
        public int Second = 51;

        internal void Deconstruct(out int first, out int second)
        {
            first = First;
            second = Second;
        }
    }

    /// <summary>
    /// Collection filled through an internal extension Add in an unpassed file.
    /// </summary>
    public sealed class HotReloadUnpassedBag : IEnumerable
    {
        public int Sum = 101;

        public IEnumerator GetEnumerator()
        {
            return Array.Empty<int>().GetEnumerator();
        }
    }

    /// <summary>
    /// Collection with an internal instance Add, declared in an unpassed file.
    /// </summary>
    public sealed class HotReloadUnpassedInternalAddBag : IEnumerable
    {
        public int Sum = 201;

        internal void Add(int value)
        {
            Sum += value;
        }

        public IEnumerator GetEnumerator()
        {
            return Array.Empty<int>().GetEnumerator();
        }
    }

    /// <summary>
    /// Query source whose Select is an internal extension method in an unpassed file.
    /// </summary>
    public sealed class HotReloadUnpassedSource
    {
        public int Value = 8;
    }

    /// <summary>
    /// Query source whose only query method is an internal extension Where in an unpassed file.
    /// </summary>
    public sealed class HotReloadUnpassedFilterSource
    {
        public int Value = 10;
    }

    /// <summary>
    /// Ref struct resource whose pattern Dispose is internal, declared in an unpassed file.
    /// </summary>
    public ref struct HotReloadUnpassedRefResource
    {
        internal void Dispose()
        {
        }
    }

    /// <summary>
    /// Resource without IAsyncDisposable whose pattern DisposeAsync is internal, declared in an
    /// unpassed file.
    /// </summary>
    public sealed class HotReloadUnpassedAsyncResource
    {
        internal ValueTask DisposeAsync()
        {
            return default;
        }
    }
}
