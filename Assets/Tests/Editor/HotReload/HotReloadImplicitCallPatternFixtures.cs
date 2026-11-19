using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace System.Runtime.CompilerServices
{
    /// <summary>
    /// The Unity 4.8 reference profile has no InterpolatedStringHandlerAttribute, and the compiler
    /// recognizes a handler type by this attribute's full name, so the fixtures declare it.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
    internal sealed class InterpolatedStringHandlerAttribute : Attribute
    {
    }
}

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Interpolated string handler whose constructor is internal.
    /// </summary>
    [InterpolatedStringHandler]
    public struct HotReloadPatternHandlerInternalCtor
    {
        public int Total;

        internal HotReloadPatternHandlerInternalCtor(int literalLength, int formattedCount)
        {
            Total = literalLength;
        }

        public void AppendLiteral(string value)
        {
            Total += value.Length;
        }

        public void AppendFormatted(int value)
        {
            Total += value;
        }
    }

    /// <summary>
    /// Interpolated string handler whose AppendLiteral is internal.
    /// </summary>
    [InterpolatedStringHandler]
    public struct HotReloadPatternHandlerInternalAppendLiteral
    {
        public int Total;

        public HotReloadPatternHandlerInternalAppendLiteral(int literalLength, int formattedCount)
        {
            Total = literalLength;
        }

        internal void AppendLiteral(string value)
        {
            Total += value.Length;
        }

        public void AppendFormatted(int value)
        {
            Total += value;
        }
    }

    /// <summary>
    /// Interpolated string handler whose AppendFormatted is internal.
    /// </summary>
    [InterpolatedStringHandler]
    public struct HotReloadPatternHandlerInternalAppendFormatted
    {
        public int Total;

        public HotReloadPatternHandlerInternalAppendFormatted(int literalLength, int formattedCount)
        {
            Total = literalLength;
        }

        public void AppendLiteral(string value)
        {
            Total += value.Length;
        }

        internal void AppendFormatted(int value)
        {
            Total += value;
        }
    }

    /// <summary>
    /// Ref struct interpolated string handler whose AppendFormatted is internal, the usual shape of
    /// a real handler.
    /// </summary>
    [InterpolatedStringHandler]
    public ref struct HotReloadPatternRefHandlerInternalAppendFormatted
    {
        public int Total;

        public HotReloadPatternRefHandlerInternalAppendFormatted(int literalLength, int formattedCount)
        {
            Total = literalLength;
        }

        public void AppendLiteral(string value)
        {
            Total += value.Length;
        }

        internal void AppendFormatted(int value)
        {
            Total += value;
        }
    }

    /// <summary>
    /// Interpolated string handler whose members are all public.
    /// </summary>
    [InterpolatedStringHandler]
    public struct HotReloadPatternPublicHandler
    {
        public int Total;

        public HotReloadPatternPublicHandler(int literalLength, int formattedCount)
        {
            Total = literalLength;
        }

        public void AppendLiteral(string value)
        {
            Total += value.Length;
        }

        public void AppendFormatted(int value)
        {
            Total += value;
        }
    }

    /// <summary>
    /// Public methods that take each handler, so the only inaccessible call is the handler's own.
    /// </summary>
    public static class HotReloadPatternHandlerSinks
    {
        public static int MeasureCtor(HotReloadPatternHandlerInternalCtor handler)
        {
            return handler.Total;
        }

        public static int MeasureLiteral(HotReloadPatternHandlerInternalAppendLiteral handler)
        {
            return handler.Total;
        }

        public static int MeasureFormatted(HotReloadPatternHandlerInternalAppendFormatted handler)
        {
            return handler.Total;
        }

        public static int MeasureRefFormatted(HotReloadPatternRefHandlerInternalAppendFormatted handler)
        {
            return handler.Total;
        }

        public static int MeasurePublic(HotReloadPatternPublicHandler handler)
        {
            return handler.Total;
        }
    }

    /// <summary>
    /// Indexable through the implicit Index support with an internal Length.
    /// </summary>
    public sealed class HotReloadPatternIndexInternalLength
    {
        internal int Length => 3;

        public int this[int index] => 100 + index;
    }

    /// <summary>
    /// Indexable through the implicit Index support with an internal Count.
    /// </summary>
    public sealed class HotReloadPatternIndexInternalCount
    {
        internal int Count => 3;

        public int this[int index] => 100 + index;
    }

    /// <summary>
    /// Sliceable through the implicit Range support with an internal Length.
    /// </summary>
    public sealed class HotReloadPatternRangeInternalLength
    {
        internal int Length => 3;

        public int Slice(int start, int length)
        {
            return start * 10 + length;
        }
    }

    /// <summary>
    /// Writable through the implicit Index support with an internal Length.
    /// </summary>
    public sealed class HotReloadPatternWritableIndexInternalLength
    {
        private readonly int[] _values = { 1, 2, 3 };

        internal int Length => _values.Length;

        public int this[int index]
        {
            get => _values[index];
            set => _values[index] = value;
        }
    }

    /// <summary>
    /// Sliceable through the implicit Range support with a public Length and an internal Slice.
    /// </summary>
    public sealed class HotReloadPatternRangeInternalSlice
    {
        public int Length => 3;

        internal int Slice(int start, int length)
        {
            return start * 10 + length;
        }
    }

    /// <summary>
    /// Indexable through the implicit Index support with a public Length and an indexer whose
    /// getter is internal.
    /// </summary>
    public sealed class HotReloadPatternIndexInternalGetter
    {
        private readonly int[] _values = { 1, 2, 3 };

        public int Length => _values.Length;

        public int this[int index]
        {
            internal get => _values[index];
            set => _values[index] = value;
        }
    }

    /// <summary>
    /// Writable through the implicit Index support with a public Length and an indexer whose
    /// setter is internal.
    /// </summary>
    public sealed class HotReloadPatternIndexInternalSetter
    {
        private readonly int[] _values = { 1, 2, 3 };

        public int Length => _values.Length;

        public int this[int index]
        {
            get => _values[index];
            internal set => _values[index] = value;
        }
    }

    /// <summary>
    /// Writable through the implicit Index support with a public Length and an internal
    /// ref-returning indexer, which a write goes through instead of a setter.
    /// </summary>
    public sealed class HotReloadPatternIndexInternalRefIndexer
    {
        private readonly int[] _values = { 1, 2, 3 };

        public int Length => _values.Length;

        internal ref int this[int index] => ref _values[index];
    }

    /// <summary>
    /// Indexable and sliceable through the implicit Index and Range support with public members.
    /// </summary>
    public sealed class HotReloadPatternPublicIndexable
    {
        public int Length => 3;

        public int this[int index] => 100 + index;

        public int Slice(int start, int length)
        {
            return start * 10 + length;
        }
    }

    /// <summary>
    /// Awaiter whose members are all public; it completes synchronously.
    /// </summary>
    public sealed class HotReloadPatternReadyAwaiter : INotifyCompletion
    {
        private readonly bool _result;

        public HotReloadPatternReadyAwaiter(bool result)
        {
            _result = result;
        }

        public bool IsCompleted => true;

        public bool GetResult()
        {
            return _result;
        }

        public void OnCompleted(Action continuation)
        {
            continuation();
        }
    }

    /// <summary>
    /// Awaiter whose IsCompleted is internal; it completes synchronously.
    /// </summary>
    public sealed class HotReloadPatternAwaiterInternalIsCompleted : INotifyCompletion
    {
        private readonly bool _result;

        public HotReloadPatternAwaiterInternalIsCompleted(bool result)
        {
            _result = result;
        }

        internal bool IsCompleted => true;

        public bool GetResult()
        {
            return _result;
        }

        public void OnCompleted(Action continuation)
        {
            continuation();
        }
    }

    /// <summary>
    /// Awaiter whose GetResult is internal; it completes synchronously.
    /// </summary>
    public sealed class HotReloadPatternAwaiterInternalGetResult : INotifyCompletion
    {
        private readonly bool _result;

        public HotReloadPatternAwaiterInternalGetResult(bool result)
        {
            _result = result;
        }

        public bool IsCompleted => true;

        internal bool GetResult()
        {
            return _result;
        }

        public void OnCompleted(Action continuation)
        {
            continuation();
        }
    }

    /// <summary>
    /// Awaitable whose GetAwaiter is internal.
    /// </summary>
    public sealed class HotReloadPatternAwaitableInternalGetAwaiter
    {
        private readonly bool _result;

        public HotReloadPatternAwaitableInternalGetAwaiter(bool result)
        {
            _result = result;
        }

        internal HotReloadPatternReadyAwaiter GetAwaiter()
        {
            return new HotReloadPatternReadyAwaiter(_result);
        }
    }

    /// <summary>
    /// Awaitable whose awaiter has an internal IsCompleted.
    /// </summary>
    public sealed class HotReloadPatternAwaitableInternalIsCompleted
    {
        private readonly bool _result;

        public HotReloadPatternAwaitableInternalIsCompleted(bool result)
        {
            _result = result;
        }

        public HotReloadPatternAwaiterInternalIsCompleted GetAwaiter()
        {
            return new HotReloadPatternAwaiterInternalIsCompleted(_result);
        }
    }

    /// <summary>
    /// Awaitable whose awaiter has an internal GetResult.
    /// </summary>
    public sealed class HotReloadPatternAwaitableInternalGetResult
    {
        private readonly bool _result;

        public HotReloadPatternAwaitableInternalGetResult(bool result)
        {
            _result = result;
        }

        public HotReloadPatternAwaiterInternalGetResult GetAwaiter()
        {
            return new HotReloadPatternAwaiterInternalGetResult(_result);
        }
    }

    /// <summary>
    /// Awaitable whose members, and its awaiter's, are all public.
    /// </summary>
    public sealed class HotReloadPatternPublicAwaitable
    {
        private readonly bool _result;

        public HotReloadPatternPublicAwaitable(bool result)
        {
            _result = result;
        }

        public HotReloadPatternReadyAwaiter GetAwaiter()
        {
            return new HotReloadPatternReadyAwaiter(_result);
        }
    }

    /// <summary>
    /// Resource with a public pattern DisposeAsync whose awaitable has an internal GetAwaiter.
    /// </summary>
    public sealed class HotReloadPatternResourceInternalGetAwaiter
    {
        public HotReloadPatternAwaitableInternalGetAwaiter DisposeAsync()
        {
            return new HotReloadPatternAwaitableInternalGetAwaiter(true);
        }
    }

    /// <summary>
    /// Resource with a public pattern DisposeAsync whose awaiter has an internal IsCompleted.
    /// </summary>
    public sealed class HotReloadPatternResourceInternalIsCompleted
    {
        public HotReloadPatternAwaitableInternalIsCompleted DisposeAsync()
        {
            return new HotReloadPatternAwaitableInternalIsCompleted(true);
        }
    }

    /// <summary>
    /// Resource with a public pattern DisposeAsync whose awaiter has an internal GetResult.
    /// </summary>
    public sealed class HotReloadPatternResourceInternalGetResult
    {
        public HotReloadPatternAwaitableInternalGetResult DisposeAsync()
        {
            return new HotReloadPatternAwaitableInternalGetResult(true);
        }
    }

    /// <summary>
    /// Resource with a public pattern DisposeAsync whose awaitable is public throughout.
    /// </summary>
    public sealed class HotReloadPatternPublicResource
    {
        public HotReloadPatternPublicAwaitable DisposeAsync()
        {
            return new HotReloadPatternPublicAwaitable(true);
        }
    }

    /// <summary>
    /// Async sequence whose MoveNextAsync awaitable has an internal GetAwaiter.
    /// </summary>
    public sealed class HotReloadPatternSequenceInternalGetAwaiter
    {
        public Enumerator GetAsyncEnumerator()
        {
            return new Enumerator();
        }

        /// <summary>
        /// Yields 1 and 2.
        /// </summary>
        public sealed class Enumerator
        {
            public int Current { get; private set; }

            public HotReloadPatternAwaitableInternalGetAwaiter MoveNextAsync()
            {
                Current++;
                return new HotReloadPatternAwaitableInternalGetAwaiter(Current <= 2);
            }
        }
    }

    /// <summary>
    /// Async sequence whose MoveNextAsync awaiter has an internal IsCompleted.
    /// </summary>
    public sealed class HotReloadPatternSequenceInternalIsCompleted
    {
        public Enumerator GetAsyncEnumerator()
        {
            return new Enumerator();
        }

        /// <summary>
        /// Yields 1 and 2.
        /// </summary>
        public sealed class Enumerator
        {
            public int Current { get; private set; }

            public HotReloadPatternAwaitableInternalIsCompleted MoveNextAsync()
            {
                Current++;
                return new HotReloadPatternAwaitableInternalIsCompleted(Current <= 2);
            }
        }
    }

    /// <summary>
    /// Async sequence whose MoveNextAsync awaiter has an internal GetResult.
    /// </summary>
    public sealed class HotReloadPatternSequenceInternalGetResult
    {
        public Enumerator GetAsyncEnumerator()
        {
            return new Enumerator();
        }

        /// <summary>
        /// Yields 1 and 2.
        /// </summary>
        public sealed class Enumerator
        {
            public int Current { get; private set; }

            public HotReloadPatternAwaitableInternalGetResult MoveNextAsync()
            {
                Current++;
                return new HotReloadPatternAwaitableInternalGetResult(Current <= 2);
            }
        }
    }

    /// <summary>
    /// Async sequence whose enumerator's pattern DisposeAsync awaitable has an internal GetAwaiter,
    /// while MoveNextAsync is public throughout.
    /// </summary>
    public sealed class HotReloadPatternSequenceDisposeInternalGetAwaiter
    {
        public Enumerator GetAsyncEnumerator()
        {
            return new Enumerator();
        }

        /// <summary>
        /// Yields 1 and 2.
        /// </summary>
        public sealed class Enumerator
        {
            public int Current { get; private set; }

            public HotReloadPatternPublicAwaitable MoveNextAsync()
            {
                Current++;
                return new HotReloadPatternPublicAwaitable(Current <= 2);
            }

            public HotReloadPatternAwaitableInternalGetAwaiter DisposeAsync()
            {
                return new HotReloadPatternAwaitableInternalGetAwaiter(true);
            }
        }
    }

    /// <summary>
    /// Async sequence whose enumerator's members and awaitables are all public.
    /// </summary>
    public sealed class HotReloadPatternPublicSequence
    {
        public Enumerator GetAsyncEnumerator()
        {
            return new Enumerator();
        }

        /// <summary>
        /// Yields 1 and 2.
        /// </summary>
        public sealed class Enumerator
        {
            public int Current { get; private set; }

            public HotReloadPatternPublicAwaitable MoveNextAsync()
            {
                Current++;
                return new HotReloadPatternPublicAwaitable(Current <= 2);
            }

            public HotReloadPatternPublicAwaitable DisposeAsync()
            {
                return new HotReloadPatternPublicAwaitable(true);
            }
        }
    }

    /// <summary>
    /// Compiled host whose lambdas and async methods reach an internal member only through an
    /// interpolated string handler, the implicit Index or Range support, or the awaiter that
    /// await using and await foreach call. Each body holds a 4-digit token unique in this file,
    /// which the tests edit. Every awaiter completes synchronously, so no continuation is queued.
    /// </summary>
    public sealed class HotReloadImplicitCallPatternHost
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureBuildsHandlerWithInternalCtor()
        {
            Func<int> read = () => HotReloadPatternHandlerSinks.MeasureCtor($"ab{7101}");
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public async Task<int> AsyncBuildsHandlerWithInternalCtor()
        {
            await Task.CompletedTask;
            return HotReloadPatternHandlerSinks.MeasureCtor($"ab{7111}");
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureBuildsHandlerWithInternalAppendLiteral()
        {
            Func<int> read = () => HotReloadPatternHandlerSinks.MeasureLiteral($"ab{7121}");
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public async Task<int> AsyncBuildsHandlerWithInternalAppendLiteral()
        {
            await Task.CompletedTask;
            return HotReloadPatternHandlerSinks.MeasureLiteral($"ab{7131}");
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureBuildsHandlerWithInternalAppendFormatted()
        {
            Func<int> read = () => HotReloadPatternHandlerSinks.MeasureFormatted($"ab{7141}");
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public async Task<int> AsyncBuildsHandlerWithInternalAppendFormatted()
        {
            await Task.CompletedTask;
            return HotReloadPatternHandlerSinks.MeasureFormatted($"ab{7151}");
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureBuildsRefHandlerWithInternalAppendFormatted()
        {
            Func<int> read = () => HotReloadPatternHandlerSinks.MeasureRefFormatted($"ab{7161}");
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public async Task<int> AsyncBuildsRefHandlerWithInternalAppendFormatted()
        {
            await Task.CompletedTask;
            return HotReloadPatternHandlerSinks.MeasureRefFormatted($"ab{7171}");
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureConvertsToHandlerLocalWithInternalAppendFormatted()
        {
            Func<int> read = () =>
            {
                HotReloadPatternHandlerInternalAppendFormatted handler = $"ab{7181}";
                return handler.Total;
            };
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public async Task<int> AsyncConvertsToHandlerLocalWithInternalAppendFormatted()
        {
            await Task.CompletedTask;
            HotReloadPatternHandlerInternalAppendFormatted handler = $"ab{7191}";
            return handler.Total;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureBuildsPublicHandler()
        {
            Func<int> read = () => HotReloadPatternHandlerSinks.MeasurePublic($"ab{7196}");
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureIndexesFromEndWithInternalLength()
        {
            Func<int> read = () => new HotReloadPatternIndexInternalLength()[^1] + 7201;
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public async Task<int> AsyncIndexesFromEndWithInternalLength()
        {
            await Task.CompletedTask;
            return new HotReloadPatternIndexInternalLength()[^1] + 7211;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureIndexesFromEndWithInternalCount()
        {
            Func<int> read = () => new HotReloadPatternIndexInternalCount()[^1] + 7221;
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public async Task<int> AsyncIndexesFromEndWithInternalCount()
        {
            await Task.CompletedTask;
            return new HotReloadPatternIndexInternalCount()[^1] + 7231;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureSlicesRangeWithInternalLength()
        {
            Func<int> read = () => new HotReloadPatternRangeInternalLength()[1..^1] + 7241;
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public async Task<int> AsyncSlicesRangeWithInternalLength()
        {
            await Task.CompletedTask;
            return new HotReloadPatternRangeInternalLength()[1..^1] + 7251;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureAssignsFromEndWithInternalLength()
        {
            Func<int> read = () =>
            {
                HotReloadPatternWritableIndexInternalLength values = new HotReloadPatternWritableIndexInternalLength();
                values[^1] = 7261;
                return values[2];
            };
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public async Task<int> AsyncAssignsFromEndWithInternalLength()
        {
            await Task.CompletedTask;
            HotReloadPatternWritableIndexInternalLength values = new HotReloadPatternWritableIndexInternalLength();
            values[^1] = 7271;
            return values[2];
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureBuildsHandlerFromInterpolatedAddition()
        {
            Func<int> read = () => HotReloadPatternHandlerSinks.MeasureFormatted($"ab" + $"{7601}");
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public async Task<int> AsyncBuildsHandlerFromInterpolatedAddition()
        {
            await Task.CompletedTask;
            return HotReloadPatternHandlerSinks.MeasureFormatted($"ab" + $"{7611}");
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureIndexesConditionallyFromEndWithInternalLength()
        {
            HotReloadPatternIndexInternalLength values = new HotReloadPatternIndexInternalLength();
            Func<int> read = () => (values?[^1] ?? 0) + 7621;
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public async Task<int> AsyncIndexesConditionallyFromEndWithInternalLength()
        {
            await Task.CompletedTask;
            HotReloadPatternIndexInternalLength values = new HotReloadPatternIndexInternalLength();
            return (values?[^1] ?? 0) + 7631;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureIndexesConditionallyFromEndWithPublicMembers()
        {
            HotReloadPatternPublicIndexable values = new HotReloadPatternPublicIndexable();
            Func<int> read = () => (values?[^1] ?? 0) + 7641;
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureSlicesRangeWithInternalSlice()
        {
            Func<int> read = () => new HotReloadPatternRangeInternalSlice()[1..^1] + 7651;
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public async Task<int> AsyncSlicesRangeWithInternalSlice()
        {
            await Task.CompletedTask;
            return new HotReloadPatternRangeInternalSlice()[1..^1] + 7661;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureIndexesFromEndWithInternalGetter()
        {
            Func<int> read = () => new HotReloadPatternIndexInternalGetter()[^1] + 7671;
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public async Task<int> AsyncIndexesFromEndWithInternalGetter()
        {
            await Task.CompletedTask;
            return new HotReloadPatternIndexInternalGetter()[^1] + 7681;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureAssignsFromEndWithInternalSetter()
        {
            Func<int> read = () =>
            {
                HotReloadPatternIndexInternalSetter values = new HotReloadPatternIndexInternalSetter();
                values[^1] = 7691;
                return values[2];
            };
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public async Task<int> AsyncAssignsFromEndWithInternalSetter()
        {
            await Task.CompletedTask;
            HotReloadPatternIndexInternalSetter values = new HotReloadPatternIndexInternalSetter();
            values[^1] = 7701;
            return values[2];
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureSlicesFromStartWithInternalLength()
        {
            // Why this stays patchable: a range whose endpoints both count from the start is lowered
            // to Slice(start, end - start) without reading Length.
            Func<int> read = () => new HotReloadPatternRangeInternalLength()[1..2] + 7721;
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public async Task<int> AsyncSlicesFromStartWithInternalLength()
        {
            await Task.CompletedTask;
            return new HotReloadPatternRangeInternalLength()[1..2] + 7731;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureSlicesOpenStartWithInternalLength()
        {
            Func<int> read = () => new HotReloadPatternRangeInternalLength()[..2] + 7741;
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public async Task<int> AsyncSlicesOpenStartWithInternalLength()
        {
            await Task.CompletedTask;
            return new HotReloadPatternRangeInternalLength()[..2] + 7751;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureSlicesOpenEndWithInternalLength()
        {
            Func<int> read = () => new HotReloadPatternRangeInternalLength()[1..] + 7761;
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureSlicesFromEndToStartWithInternalLength()
        {
            Func<int> read = () => new HotReloadPatternRangeInternalLength()[^2..2] + 7771;
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public async Task<int> AsyncSlicesFromEndToStartWithInternalLength()
        {
            await Task.CompletedTask;
            return new HotReloadPatternRangeInternalLength()[^2..2] + 7781;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureCastsToHandlerWithInternalAppendFormatted()
        {
            Func<int> read = () => HotReloadPatternHandlerSinks.MeasureFormatted(
                (HotReloadPatternHandlerInternalAppendFormatted)$"ab{7791}");
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public async Task<int> AsyncCastsToHandlerWithInternalAppendFormatted()
        {
            await Task.CompletedTask;
            return HotReloadPatternHandlerSinks.MeasureFormatted(
                (HotReloadPatternHandlerInternalAppendFormatted)$"ab{7801}");
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureAssignsParenthesizedFromEndWithInternalGetter()
        {
            Func<int> read = () =>
            {
                HotReloadPatternIndexInternalGetter values = new HotReloadPatternIndexInternalGetter();
                (values[^1]) = 7811;
                return values.Length;
            };
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public async Task<int> AsyncAssignsParenthesizedFromEndWithInternalGetter()
        {
            await Task.CompletedTask;
            HotReloadPatternIndexInternalGetter values = new HotReloadPatternIndexInternalGetter();
            (values[^1]) = 7821;
            return values.Length;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureAssignsParenthesizedFromEndWithInternalRefIndexer()
        {
            Func<int> read = () =>
            {
                HotReloadPatternIndexInternalRefIndexer values = new HotReloadPatternIndexInternalRefIndexer();
                (values[^1]) = 7831;
                return values.Length;
            };
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public async Task<int> AsyncAssignsParenthesizedFromEndWithInternalRefIndexer()
        {
            await Task.CompletedTask;
            HotReloadPatternIndexInternalRefIndexer values = new HotReloadPatternIndexInternalRefIndexer();
            (values[^1]) = 7841;
            return values.Length;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureIndexesAndSlicesPublicMembers()
        {
            Func<int> read = () =>
            {
                HotReloadPatternPublicIndexable values = new HotReloadPatternPublicIndexable();
                return values[^1] + values[1..^1] + 7281;
            };
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public Task<int> ClosureAwaitUsesDisposeAsyncWithInternalGetAwaiter()
        {
            Func<Task<int>> read = async () =>
            {
                await using (new HotReloadPatternResourceInternalGetAwaiter())
                {
                }

                return 7301;
            };
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public async Task<int> AsyncAwaitUsesDisposeAsyncWithInternalGetAwaiter()
        {
            await using (new HotReloadPatternResourceInternalGetAwaiter())
            {
            }

            return 7311;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public Task<int> ClosureAwaitUsesDisposeAsyncWithInternalIsCompleted()
        {
            Func<Task<int>> read = async () =>
            {
                await using (new HotReloadPatternResourceInternalIsCompleted())
                {
                }

                return 7321;
            };
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public async Task<int> AsyncAwaitUsesDisposeAsyncWithInternalIsCompleted()
        {
            await using (new HotReloadPatternResourceInternalIsCompleted())
            {
            }

            return 7331;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public Task<int> ClosureAwaitUsesDisposeAsyncWithInternalGetResult()
        {
            Func<Task<int>> read = async () =>
            {
                await using (new HotReloadPatternResourceInternalGetResult())
                {
                }

                return 7341;
            };
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public async Task<int> AsyncAwaitUsesDisposeAsyncWithInternalGetResult()
        {
            await using (new HotReloadPatternResourceInternalGetResult())
            {
            }

            return 7351;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public Task<int> ClosureAwaitUsingDeclarationWithInternalGetAwaiter()
        {
            Func<Task<int>> read = async () =>
            {
                await using HotReloadPatternResourceInternalGetAwaiter resource =
                    new HotReloadPatternResourceInternalGetAwaiter();
                return 7361;
            };
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public async Task<int> AsyncAwaitUsingDeclarationWithInternalGetAwaiter()
        {
            await using HotReloadPatternResourceInternalGetAwaiter resource =
                new HotReloadPatternResourceInternalGetAwaiter();
            return 7371;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public Task<int> ClosureAwaitUsesPublicDisposeAsync()
        {
            Func<Task<int>> read = async () =>
            {
                await using (new HotReloadPatternPublicResource())
                {
                }

                return 7381;
            };
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public Task<int> ClosureAwaitForEachesMoveNextWithInternalGetAwaiter()
        {
            Func<Task<int>> read = async () =>
            {
                int sum = 7401;
                await foreach (int value in new HotReloadPatternSequenceInternalGetAwaiter())
                {
                    sum += value;
                }

                return sum;
            };
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public async Task<int> AsyncAwaitForEachesMoveNextWithInternalGetAwaiter()
        {
            int sum = 7411;
            await foreach (int value in new HotReloadPatternSequenceInternalGetAwaiter())
            {
                sum += value;
            }

            return sum;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public Task<int> ClosureAwaitForEachesMoveNextWithInternalIsCompleted()
        {
            Func<Task<int>> read = async () =>
            {
                int sum = 7421;
                await foreach (int value in new HotReloadPatternSequenceInternalIsCompleted())
                {
                    sum += value;
                }

                return sum;
            };
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public async Task<int> AsyncAwaitForEachesMoveNextWithInternalIsCompleted()
        {
            int sum = 7431;
            await foreach (int value in new HotReloadPatternSequenceInternalIsCompleted())
            {
                sum += value;
            }

            return sum;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public Task<int> ClosureAwaitForEachesMoveNextWithInternalGetResult()
        {
            Func<Task<int>> read = async () =>
            {
                int sum = 7441;
                await foreach (int value in new HotReloadPatternSequenceInternalGetResult())
                {
                    sum += value;
                }

                return sum;
            };
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public async Task<int> AsyncAwaitForEachesMoveNextWithInternalGetResult()
        {
            int sum = 7451;
            await foreach (int value in new HotReloadPatternSequenceInternalGetResult())
            {
                sum += value;
            }

            return sum;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public Task<int> ClosureAwaitForEachesEnumeratorDisposeWithInternalGetAwaiter()
        {
            Func<Task<int>> read = async () =>
            {
                int sum = 7461;
                await foreach (int value in new HotReloadPatternSequenceDisposeInternalGetAwaiter())
                {
                    sum += value;
                }

                return sum;
            };
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public async Task<int> AsyncAwaitForEachesEnumeratorDisposeWithInternalGetAwaiter()
        {
            int sum = 7471;
            await foreach (int value in new HotReloadPatternSequenceDisposeInternalGetAwaiter())
            {
                sum += value;
            }

            return sum;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public Task<int> ClosureAwaitForEachesPublicSequence()
        {
            Func<Task<int>> read = async () =>
            {
                int sum = 7481;
                await foreach (int value in new HotReloadPatternPublicSequence())
                {
                    sum += value;
                }

                return sum;
            };
            return read();
        }
    }
}
