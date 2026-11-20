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
    /// Internal six-out Deconstruct with a new() constraint, declared before the one the two
    /// classes without a public parameterless constructor actually call.
    /// </summary>
    public static class HotReloadUnpassedNewConstrainedExtensions
    {
        internal static void Deconstruct<T>(
            this T value,
            out int first,
            out int second,
            out int third,
            out int fourth,
            out int fifth,
            out int sixth)
            where T : new()
        {
            first = value == null ? 0 : 1;
            second = 2;
            third = 3;
            fourth = 4;
            fifth = 5;
            sixth = 6;
        }

        internal static void Deconstruct<T>(
            this T value,
            out int first,
            out int second,
            out int third,
            out int fourth,
            out int fifth,
            out int sixth,
            out int seventh)
            where T : unmanaged
        {
            first = value.GetHashCode() == 0 ? 0 : 1;
            second = 2;
            third = 3;
            fourth = 4;
            fifth = 5;
            sixth = 6;
            seventh = 7;
        }
    }

    /// <summary>
    /// The six-out and seven-out Deconstruct methods the receivers below actually call.
    /// </summary>
    public static class HotReloadUnpassedConstraintTargetExtensions
    {
        internal static void Deconstruct(
            this HotReloadUnpassedNoDefaultConstructor value,
            out int first,
            out int second,
            out int third,
            out int fourth,
            out int fifth,
            out int sixth)
        {
            first = value.Seed;
            second = 2;
            third = 3;
            fourth = 4;
            fifth = 5;
            sixth = 6;
        }

        internal static void Deconstruct(
            this HotReloadUnpassedManagedStruct value,
            out int first,
            out int second,
            out int third,
            out int fourth,
            out int fifth,
            out int sixth,
            out int seventh)
        {
            first = value.Label == null ? 0 : 1;
            second = 2;
            third = 3;
            fourth = 4;
            fifth = 5;
            sixth = 6;
            seventh = 7;
        }
    }

    /// <summary>
    /// Class without a public parameterless constructor, so it does not satisfy new().
    /// </summary>
    public sealed class HotReloadUnpassedNoDefaultConstructor
    {
        public HotReloadUnpassedNoDefaultConstructor(int seed)
        {
            Seed = seed;
        }

        public int Seed { get; }
    }

    /// <summary>
    /// Struct with a reference field, so it does not satisfy unmanaged.
    /// </summary>
    public struct HotReloadUnpassedManagedStruct
    {
        public string Label;
    }

    /// <summary>
    /// Indexable with two arguments, an int and an Index, through an internal indexer, beside an
    /// internal Length that the access never reads.
    /// </summary>
    public sealed class HotReloadUnpassedTwoArgumentIndexer
    {
        internal int Length => 3;

        internal int this[int row, System.Index column] => row * 10 + column.Value;
    }

    /// <summary>
    /// Indexable with one int argument through an internal indexer only, in an unpassed file.
    /// </summary>
    public sealed class HotReloadUnpassedInternalIntIndexer
    {
        internal int this[int index] => 100 + index;
    }

    /// <summary>
    /// Internal eight-out Deconstruct constrained to struct, declared before the one a nullable
    /// struct receiver actually calls; Nullable&lt;T&gt; does not satisfy the struct constraint.
    /// </summary>
    public static class HotReloadUnpassedStructOnlyEightOutExtensions
    {
        internal static void Deconstruct<T>(
            this T value,
            out int p1,
            out int p2,
            out int p3,
            out int p4,
            out int p5,
            out int p6,
            out int p7,
            out int p8)
            where T : struct
        {
            p1 = value.GetHashCode() == 0 ? 0 : 1;
            p2 = 2;
            p3 = 3;
            p4 = 4;
            p5 = 5;
            p6 = 6;
            p7 = 7;
            p8 = 8;
        }
    }

    /// <summary>
    /// The eight-out Deconstruct a nullable struct receiver actually calls.
    /// </summary>
    public static class HotReloadUnpassedNullableEightOutExtensions
    {
        internal static void Deconstruct(
            this HotReloadUnpassedQuint? value,
            out int p1,
            out int p2,
            out int p3,
            out int p4,
            out int p5,
            out int p6,
            out int p7,
            out int p8)
        {
            p1 = value.HasValue ? 1 : 0;
            p2 = 2;
            p3 = 3;
            p4 = 4;
            p5 = 5;
            p6 = 6;
            p7 = 7;
            p8 = 8;
        }
    }

    /// <summary>
    /// Sliceable only through an internal Range indexer, with no Length or Slice, so a range
    /// access binds to that indexer directly.
    /// </summary>
    public sealed class HotReloadUnpassedInternalRangeIndexer
    {
        internal int this[System.Range range] => range.Start.Value;
    }

    /// <summary>
    /// Internal nine-out Deconstruct over any reference type, which an array receiver binds to.
    /// </summary>
    public static class HotReloadUnpassedArrayReceiverExtensions
    {
        internal static void Deconstruct<T>(
            this T value,
            out int p1,
            out int p2,
            out int p3,
            out int p4,
            out int p5,
            out int p6,
            out int p7,
            out int p8,
            out int p9)
            where T : class
        {
            p1 = value == null ? 0 : 1;
            p2 = 2;
            p3 = 3;
            p4 = 4;
            p5 = 5;
            p6 = 6;
            p7 = 7;
            p8 = 8;
            p9 = 9;
        }
    }

    /// <summary>
    /// Base whose internal two-out Deconstruct a two-part deconstruction of the derived type calls.
    /// </summary>
    public class HotReloadUnpassedRequiredArgumentBase
    {
        internal void Deconstruct(out int first, out int second)
        {
            first = 1;
            second = 2;
        }
    }

    /// <summary>
    /// Derived type with an internal Deconstruct that has two out parameters after a required one,
    /// which a two-part deconstruction cannot call.
    /// </summary>
    public sealed class HotReloadUnpassedRequiredArgumentDerived : HotReloadUnpassedRequiredArgumentBase
    {
        internal void Deconstruct(int required, out int first, out int second)
        {
            first = required;
            second = required;
        }
    }

    /// <summary>
    /// Internal ten-out Deconstruct over any one-dimensional array.
    /// </summary>
    public static class HotReloadUnpassedArrayThisExtensions
    {
        internal static void Deconstruct<T>(
            this T[] values,
            out int p1,
            out int p2,
            out int p3,
            out int p4,
            out int p5,
            out int p6,
            out int p7,
            out int p8,
            out int p9,
            out int p10)
        {
            p1 = values.Length;
            p2 = 2;
            p3 = 3;
            p4 = 4;
            p5 = 5;
            p6 = 6;
            p7 = 7;
            p8 = 8;
            p9 = 9;
            p10 = 10;
        }
    }

    /// <summary>
    /// Internal eleven-out Deconstruct over any type with a parameterless constructor, which a
    /// type parameter constrained to new() satisfies.
    /// </summary>
    public static class HotReloadUnpassedNewOnlyExtensions
    {
        internal static void Deconstruct<T>(
            this T value,
            out int p1,
            out int p2,
            out int p3,
            out int p4,
            out int p5,
            out int p6,
            out int p7,
            out int p8,
            out int p9,
            out int p10,
            out int p11)
            where T : new()
        {
            p1 = value == null ? 0 : 1;
            p2 = 2;
            p3 = 3;
            p4 = 4;
            p5 = 5;
            p6 = 6;
            p7 = 7;
            p8 = 8;
            p9 = 9;
            p10 = 10;
            p11 = 11;
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
    /// Generic marker interface whose type argument alone tells two constrained extensions apart.
    /// </summary>
    public interface IHotReloadUnpassedMarker<T>
    {
    }

    /// <summary>
    /// First type argument of the marker interface.
    /// </summary>
    public sealed class HotReloadUnpassedFirstMark
    {
    }

    /// <summary>
    /// Second type argument of the marker interface.
    /// </summary>
    public sealed class HotReloadUnpassedSecondMark
    {
    }

    /// <summary>
    /// Internal two-out Deconstruct constrained to the marker of the first type argument, declared
    /// before the one a receiver marked by the second type argument actually calls.
    /// </summary>
    public static class HotReloadUnpassedFirstMarkExtensions
    {
        internal static void Deconstruct<T>(this T value, out int first, out int second)
            where T : IHotReloadUnpassedMarker<HotReloadUnpassedFirstMark>
        {
            first = value == null ? 0 : 1;
            second = 2;
        }
    }

    /// <summary>
    /// The two-out Deconstruct a receiver marked by the second type argument actually calls.
    /// </summary>
    public static class HotReloadUnpassedSecondMarkExtensions
    {
        internal static void Deconstruct<T>(this T value, out int first, out int second)
            where T : IHotReloadUnpassedMarker<HotReloadUnpassedSecondMark>
        {
            first = value == null ? 0 : 3;
            second = 4;
        }
    }

    /// <summary>
    /// Receiver marked by the second type argument only.
    /// </summary>
    public sealed class HotReloadUnpassedMarkedBySecond : IHotReloadUnpassedMarker<HotReloadUnpassedSecondMark>
    {
    }

    /// <summary>
    /// Internal two-out Deconstruct constrained to the marker of a tuple whose element names differ
    /// from the ones the receiver below writes, which an identity conversion ignores.
    /// </summary>
    public static class HotReloadUnpassedNamedTupleMarkExtensions
    {
        internal static void Deconstruct<T>(this T value, out int first, out int second)
            where T : IHotReloadUnpassedMarker<(int First, int Second)>
        {
            first = value == null ? 0 : 9;
            second = 10;
        }
    }

    /// <summary>
    /// Receiver marked by a tuple with other element names than the constraint above.
    /// </summary>
    public sealed class HotReloadUnpassedMarkedByNamedTuple : IHotReloadUnpassedMarker<(int Left, int Right)>
    {
    }

    /// <summary>
    /// Internal two-out Deconstruct constrained to the marker of object, which a receiver typed as
    /// the marker of dynamic meets through an identity conversion.
    /// </summary>
    public static class HotReloadUnpassedObjectMarkExtensions
    {
        internal static void Deconstruct<T>(this T value, out int first, out int second)
            where T : IHotReloadUnpassedMarker<object>
        {
            first = value == null ? 0 : 11;
            second = 12;
        }
    }

    /// <summary>
    /// Marked by object; the host holds it as the marker of dynamic.
    /// </summary>
    public sealed class HotReloadUnpassedMarkedByObject : IHotReloadUnpassedMarker<object>
    {
    }

    /// <summary>
    /// Generic tag interface for the non-generic extensions below, whose 'this' parameter types
    /// differ only in the type argument.
    /// </summary>
    public interface IHotReloadUnpassedTag<T>
    {
    }

    /// <summary>
    /// Internal two-out Deconstruct on the tag of the first type argument, declared before the one a
    /// receiver tagged by the second type argument actually calls.
    /// </summary>
    public static class HotReloadUnpassedFirstTagExtensions
    {
        internal static void Deconstruct(this IHotReloadUnpassedTag<HotReloadUnpassedFirstMark> value, out int first, out int second)
        {
            first = value == null ? 0 : 13;
            second = 14;
        }
    }

    /// <summary>
    /// The two-out Deconstruct a receiver tagged by the second type argument actually calls.
    /// </summary>
    public static class HotReloadUnpassedSecondTagExtensions
    {
        internal static void Deconstruct(this IHotReloadUnpassedTag<HotReloadUnpassedSecondMark> value, out int first, out int second)
        {
            first = value == null ? 0 : 15;
            second = 16;
        }
    }

    /// <summary>
    /// Receiver tagged by the second type argument only.
    /// </summary>
    public sealed class HotReloadUnpassedTaggedBySecond : IHotReloadUnpassedTag<HotReloadUnpassedSecondMark>
    {
    }

    /// <summary>
    /// Internal two-out Deconstruct constrained to the marker of Memory&lt;int&gt;, a type a
    /// referenced assembly also declares internally, so a lenient binding of the name is ambiguous.
    /// </summary>
    public static class HotReloadUnpassedMemoryMarkExtensions
    {
        internal static void Deconstruct<T>(this T value, out int first, out int second)
            where T : IHotReloadUnpassedMarker<System.Memory<int>>
        {
            first = value == null ? 0 : 17;
            second = 18;
        }
    }

    /// <summary>
    /// Receiver marked by Memory&lt;int&gt;.
    /// </summary>
    public sealed class HotReloadUnpassedMarkedByMemory : IHotReloadUnpassedMarker<System.Memory<int>>
    {
    }

    /// <summary>
    /// Internal two-out Deconstruct on the tag of Memory&lt;int&gt;, as a non-generic 'this' type.
    /// </summary>
    public static class HotReloadUnpassedMemoryTagExtensions
    {
        internal static void Deconstruct(this IHotReloadUnpassedTag<System.Memory<int>> value, out int first, out int second)
        {
            first = value == null ? 0 : 19;
            second = 20;
        }
    }

    /// <summary>
    /// Receiver tagged by Memory&lt;int&gt;.
    /// </summary>
    public sealed class HotReloadUnpassedTaggedByMemory : IHotReloadUnpassedTag<System.Memory<int>>
    {
    }

    /// <summary>
    /// Internal two-out Deconstruct constrained to the marker of an array of the first type argument,
    /// declared before the one for an array of the second.
    /// </summary>
    public static class HotReloadUnpassedFirstArrayMarkExtensions
    {
        internal static void Deconstruct<T>(this T value, out int first, out int second)
            where T : IHotReloadUnpassedMarker<HotReloadUnpassedFirstMark[]>
        {
            first = value == null ? 0 : 21;
            second = 22;
        }
    }

    /// <summary>
    /// The two-out Deconstruct a receiver marked by an array of the second type argument calls.
    /// </summary>
    public static class HotReloadUnpassedSecondArrayMarkExtensions
    {
        internal static void Deconstruct<T>(this T value, out int first, out int second)
            where T : IHotReloadUnpassedMarker<HotReloadUnpassedSecondMark[]>
        {
            first = value == null ? 0 : 23;
            second = 24;
        }
    }

    /// <summary>
    /// Receiver marked by an array of the second type argument.
    /// </summary>
    public sealed class HotReloadUnpassedMarkedBySecondArray : IHotReloadUnpassedMarker<HotReloadUnpassedSecondMark[]>
    {
    }

    /// <summary>
    /// Generic outer type whose nested interface differs between constructions only in the outer
    /// type argument.
    /// </summary>
    public sealed class HotReloadUnpassedOuter<T>
    {
        /// <summary>
        /// Nested interface of the generic outer type.
        /// </summary>
        public interface IInner
        {
        }
    }

    /// <summary>
    /// Internal two-out Deconstruct constrained to the inner interface of the outer type of the first
    /// type argument, declared before the one for the second.
    /// </summary>
    public static class HotReloadUnpassedFirstInnerExtensions
    {
        internal static void Deconstruct<T>(this T value, out int first, out int second)
            where T : HotReloadUnpassedOuter<HotReloadUnpassedFirstMark>.IInner
        {
            first = value == null ? 0 : 25;
            second = 26;
        }
    }

    /// <summary>
    /// The two-out Deconstruct a receiver of the inner interface of the second outer type calls.
    /// </summary>
    public static class HotReloadUnpassedSecondInnerExtensions
    {
        internal static void Deconstruct<T>(this T value, out int first, out int second)
            where T : HotReloadUnpassedOuter<HotReloadUnpassedSecondMark>.IInner
        {
            first = value == null ? 0 : 27;
            second = 28;
        }
    }

    /// <summary>
    /// Receiver of the inner interface of the outer type of the second type argument.
    /// </summary>
    public sealed class HotReloadUnpassedInnerOfSecond : HotReloadUnpassedOuter<HotReloadUnpassedSecondMark>.IInner
    {
    }

    /// <summary>
    /// Invariant interface a type implements with itself as the type argument. Unlike
    /// IComparable&lt;T&gt;, it has no variant type parameter, so only the type parameter in the
    /// constraint below keeps that constraint from a comparison of the constructed type.
    /// </summary>
    public interface IHotReloadUnpassedSelfReferencing<T>
    {
    }

    /// <summary>
    /// Internal two-out Deconstruct whose constraint names its own type parameter, so the
    /// constructed constraint never equals an interface the receiver implements.
    /// </summary>
    public static class HotReloadUnpassedSelfReferencingExtensions
    {
        internal static void Deconstruct<T>(this T value, out int first, out int second)
            where T : IHotReloadUnpassedSelfReferencing<T>
        {
            first = value == null ? 0 : 5;
            second = 6;
        }
    }

    /// <summary>
    /// Receiver referencing itself, deconstructed through the self-referencing constraint.
    /// </summary>
    public sealed class HotReloadUnpassedSelfReferencing : IHotReloadUnpassedSelfReferencing<HotReloadUnpassedSelfReferencing>
    {
    }

    /// <summary>
    /// Covariant producer interface, so a producer of a derived type is a producer of its base.
    /// </summary>
    public interface IHotReloadUnpassedProducer<out T>
    {
    }

    /// <summary>
    /// Base of the produced types.
    /// </summary>
    public class HotReloadUnpassedProducedBase
    {
    }

    /// <summary>
    /// Derived produced type.
    /// </summary>
    public sealed class HotReloadUnpassedProducedDerived : HotReloadUnpassedProducedBase
    {
    }

    /// <summary>
    /// Internal two-out Deconstruct constrained to a producer of the base type, which a producer of
    /// the derived type satisfies through variance only.
    /// </summary>
    public static class HotReloadUnpassedVariantProducerExtensions
    {
        internal static void Deconstruct<T>(this T value, out int first, out int second)
            where T : IHotReloadUnpassedProducer<HotReloadUnpassedProducedBase>
        {
            first = value == null ? 0 : 6;
            second = 7;
        }
    }

    /// <summary>
    /// Internal two-out Deconstruct constrained to an invariant list of the base type, which an
    /// array of the derived type satisfies through array covariance only.
    /// </summary>
    public static class HotReloadUnpassedCovariantArrayExtensions
    {
        internal static void Deconstruct<T>(this T value, out int first, out int second)
            where T : System.Collections.Generic.IList<HotReloadUnpassedProducedBase>
        {
            first = value.Count;
            second = 8;
        }
    }

    /// <summary>
    /// Producer of the derived type, deconstructed through the variant constraint.
    /// </summary>
    public sealed class HotReloadUnpassedDerivedProducer : IHotReloadUnpassedProducer<HotReloadUnpassedProducedDerived>
    {
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
