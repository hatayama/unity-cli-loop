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
    /// Internal three-out Deconstruct for the arity pair, declared before the two-out one so a
    /// lookup that ignores the out-parameter count finds it first.
    /// </summary>
    public static class HotReloadUnpassedArityThreeOutExtensions
    {
        internal static void Deconstruct(
            this HotReloadUnpassedArityPair pair,
            out int first,
            out int second,
            out int third)
        {
            first = pair.First;
            second = pair.First + 1;
            third = pair.First + 2;
        }
    }

    /// <summary>
    /// Internal two-out Deconstruct for the arity pair, the one a two-part deconstruction calls.
    /// </summary>
    public static class HotReloadUnpassedArityTwoOutExtensions
    {
        internal static void Deconstruct(this HotReloadUnpassedArityPair pair, out int first, out int second)
        {
            first = pair.First;
            second = pair.First + 1;
        }
    }

    /// <summary>
    /// Pair with internal two-out and three-out extension Deconstruct methods in an unpassed file.
    /// </summary>
    public sealed class HotReloadUnpassedArityPair
    {
        public int First = 5;
    }

    /// <summary>
    /// Struct indexable through the implicit Index and Range support with an internal Length, used
    /// through a nullable receiver in an unpassed file.
    /// </summary>
    public struct HotReloadUnpassedStructInternalLength
    {
        internal int Length => 3;

        public int this[int index] => 100 + index;

        public int Slice(int start, int length)
        {
            return start * 10 + length;
        }
    }

    /// <summary>
    /// Internal extension methods whose this parameter is a type parameter with a class or a struct
    /// constraint; four and five out parameters keep them apart from the other Deconstruct fixtures.
    /// </summary>
    public static class HotReloadUnpassedKindConstrainedExtensions
    {
        internal static void Deconstruct<T>(
            this T value,
            out int first,
            out int second,
            out int third,
            out int fourth)
            where T : class
        {
            first = value == null ? 0 : 1;
            second = 2;
            third = 3;
            fourth = 4;
        }

        internal static void Deconstruct<T>(
            this T value,
            out int first,
            out int second,
            out int third,
            out int fourth,
            out int fifth)
            where T : struct
        {
            first = value.GetHashCode() == 0 ? 0 : 1;
            second = 2;
            third = 3;
            fourth = 4;
            fifth = 5;
        }
    }

    /// <summary>
    /// Class deconstructed into four parts through the class-constrained generic extension.
    /// </summary>
    public sealed class HotReloadUnpassedQuad
    {
    }

    /// <summary>
    /// Struct deconstructed into five parts through the struct-constrained generic extension.
    /// </summary>
    public struct HotReloadUnpassedQuint
    {
    }

    /// <summary>
    /// Resource whose pattern DisposeAsync is an internal instance method; a public extension
    /// DisposeAsync of the same name exists but await using never calls an extension.
    /// </summary>
    public sealed class HotReloadUnpassedResourceInternalDisposeAsync
    {
        internal HotReloadPatternPublicAwaitable DisposeAsync()
        {
            return new HotReloadPatternPublicAwaitable(true);
        }
    }

    /// <summary>
    /// Public extension DisposeAsync that a speculative call binds to when the instance method is
    /// out of reach.
    /// </summary>
    public static class HotReloadUnpassedPublicDisposeAsyncExtensions
    {
        public static System.Threading.Tasks.ValueTask DisposeAsync(this HotReloadUnpassedResourceInternalDisposeAsync resource)
        {
            return default;
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
