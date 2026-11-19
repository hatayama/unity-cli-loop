namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Internal extension methods whose this parameter is a type parameter, declared in a file the
    /// implicit-call pattern layout tests do not pass to the run, so the transform worker sees
    /// this assembly through its public surface only.
    /// </summary>
    public static class HotReloadUnpassedGenericExtensions
    {
        internal static void Deconstruct<T>(this T pair, out int first, out int second)
            where T : HotReloadUnpassedGenericPair
        {
            first = pair.First;
            second = pair.Second;
        }

        internal static HotReloadUnpassedGenericEnumerator GetEnumerator<T>(this T range)
            where T : HotReloadUnpassedGenericRange
        {
            return new HotReloadUnpassedGenericEnumerator(range.Count);
        }
    }

    /// <summary>
    /// Internal extension method whose this parameter is an unconstrained type parameter, in a
    /// file the pattern layout tests do not pass.
    /// </summary>
    public static class HotReloadUnpassedUnconstrainedExtensions
    {
        // Why three out parameters: an unconstrained this T accepts every receiver, and only a
        // three-part deconstruction can bind to this one.
        internal static void Deconstruct<T>(this T value, out int first, out int second, out int third)
        {
            first = value == null ? 0 : 25;
            second = 35;
            third = 45;
        }
    }

    /// <summary>
    /// Indexable through the implicit Index support with an internal int indexer, in an unpassed
    /// file.
    /// </summary>
    public sealed class HotReloadUnpassedIndexInternalIndexer
    {
        public int Length => 3;

        internal int this[int index] => 100 + index;
    }

    /// <summary>
    /// Sliceable through the implicit Range support with an internal Slice, in an unpassed file.
    /// </summary>
    public sealed class HotReloadUnpassedRangeInternalSlice
    {
        public int Length => 3;

        internal int Slice(int start, int length)
        {
            return start * 10 + length;
        }
    }

    /// <summary>
    /// Pair deconstructed through a constrained generic extension Deconstruct in an unpassed file.
    /// </summary>
    public class HotReloadUnpassedGenericPair
    {
        public int First = 24;
        public int Second = 34;
    }

    /// <summary>
    /// Range enumerated through a constrained generic extension GetEnumerator in an unpassed file.
    /// </summary>
    public class HotReloadUnpassedGenericRange
    {
        public int Count = 3;
    }

    /// <summary>
    /// Deconstructed into three parts through an unconstrained generic extension in an unpassed
    /// file.
    /// </summary>
    public sealed class HotReloadUnpassedGenericTriple
    {
    }

    /// <summary>
    /// Async sequence with public pattern members whose elements deconstruct only through the
    /// internal generic extension Deconstruct above. Yields one pair.
    /// </summary>
    public sealed class HotReloadUnpassedPairSequence
    {
        public Enumerator GetAsyncEnumerator()
        {
            return new Enumerator();
        }

        /// <summary>
        /// Yields one pair.
        /// </summary>
        public sealed class Enumerator
        {
            private int _moves;

            public HotReloadUnpassedGenericPair Current { get; } = new HotReloadUnpassedGenericPair();

            public HotReloadPatternPublicAwaitable MoveNextAsync()
            {
                _moves++;
                return new HotReloadPatternPublicAwaitable(_moves <= 1);
            }

            public HotReloadPatternPublicAwaitable DisposeAsync()
            {
                return new HotReloadPatternPublicAwaitable(true);
            }
        }
    }

    /// <summary>
    /// Enumerator returned by the generic extension GetEnumerator; yields 1 to Count.
    /// </summary>
    public sealed class HotReloadUnpassedGenericEnumerator
    {
        private readonly int _count;

        public HotReloadUnpassedGenericEnumerator(int count)
        {
            _count = count;
        }

        public int Current { get; private set; }

        public bool MoveNext()
        {
            Current++;
            return Current <= _count;
        }
    }
}
