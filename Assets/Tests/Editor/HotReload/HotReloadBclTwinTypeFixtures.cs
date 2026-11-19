using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// A ref struct declared in this assembly. No referenced assembly carries a copy of it, so it is
    /// the oracle for the Span shapes below: whatever row it gets is the row a Span argument must get.
    /// </summary>
    public ref struct HotReloadBclTwinUserRef
    {
        public int X;
    }

    /// <summary>
    /// Public class whose internal members take BCL types that a referenced assembly also declares
    /// internally (Span, ReadOnlySpan, Memory), next to oracle members taking types it does not
    /// declare (ArraySegment, a user ref struct).
    /// </summary>
    public static class HotReloadBclTwinTargets
    {
        internal static int SpanLength(ReadOnlySpan<int> span)
        {
            return span.Length;
        }

        internal static int SpanLengthPlus(this ReadOnlySpan<int> span, int add)
        {
            return span.Length + add;
        }

        internal static int PlusSpan(this int value, ReadOnlySpan<int> span)
        {
            return value + span.Length;
        }

        internal static int MutableSpanLength(Span<int> span)
        {
            return span.Length;
        }

        internal static int UserRefLength(HotReloadBclTwinUserRef value)
        {
            return value.X;
        }

        internal static int MemoryLength(Memory<int> memory)
        {
            return memory.Length;
        }

        internal static int SegmentLength(ArraySegment<int> segment)
        {
            return segment.Count;
        }

        internal static int ParamsMemoryLength(params Memory<int>[] memories)
        {
            return memories.Length;
        }

        internal static int ParamsSegmentLength(params ArraySegment<int>[] segments)
        {
            return segments.Length;
        }

        internal static int StringLength(string value)
        {
            return value.Length;
        }

        internal static int Identity(int value)
        {
            return value;
        }

        public static int PublicSpanLength(ReadOnlySpan<int> span)
        {
            return span.Length;
        }
    }

    /// <summary>
    /// Public class with internal constructors taking a BCL twin type or its oracle type.
    /// </summary>
    public sealed class HotReloadBclTwinBox
    {
        public int Length;

        internal HotReloadBclTwinBox(Memory<int> memory)
        {
            Length = memory.Length;
        }

        internal HotReloadBclTwinBox(ArraySegment<int> segment)
        {
            Length = segment.Count;
        }

        internal HotReloadBclTwinBox(ReadOnlySpan<int> span)
        {
            Length = span.Length;
        }

        internal HotReloadBclTwinBox(HotReloadBclTwinUserRef value)
        {
            Length = value.X;
        }
    }

    /// <summary>
    /// Public class with internal indexer setters taking a BCL twin type or its oracle type, set from
    /// an object initializer.
    /// </summary>
    public sealed class HotReloadBclTwinIndexed
    {
        public int Stored;

        internal int this[Memory<int> memory]
        {
            set => Stored = memory.Length + value;
        }

        internal int this[ArraySegment<int> segment]
        {
            set => Stored = segment.Count + value;
        }
    }

    /// <summary>
    /// Compiled host whose lambdas and async methods reach non-public members with BCL twin type
    /// arguments. Each body's 4-digit token is unique in this file and is the value the tests edit.
    /// </summary>
    public sealed class HotReloadBclTwinHost
    {
        private int _value = 3;

        private int[] _ints = { 1, 2 };

        private int _stored;

        private Memory<int> _memory;

        private Memory<int> MemoryProperty { get; set; }

        private int this[Memory<int> memory]
        {
            get => memory.Length;
            set => _stored = memory.Length + value;
        }

        private int this[ArraySegment<int> segment]
        {
            get => segment.Count;
            set => _stored = segment.Count + value;
        }

        private int this[ReadOnlySpan<int> span] => span.Length;

        private int this[HotReloadBclTwinUserRef value] => value.X;

        public int Stored => _stored;

        private int PrivateSpanLength(ReadOnlySpan<int> span)
        {
            return span.Length;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureCallsInternalMethodWithSpan()
        {
            Func<int> read = () => HotReloadBclTwinTargets.SpanLength(new ReadOnlySpan<int>(_ints)) + 6011;
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureCallsInternalExtensionOnSpan()
        {
            Func<int> read = () => new ReadOnlySpan<int>(_ints).SpanLengthPlus(6021);
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureCallsInternalExtensionWithSpan()
        {
            Func<int> read = () => _value.PlusSpan(new ReadOnlySpan<int>(_ints)) + 6031;
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureCallsPrivateMethodWithSpan()
        {
            Func<int> read = () => PrivateSpanLength(new ReadOnlySpan<int>(_ints)) + 6041;
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public async Task<int> AsyncCallsInternalMethodWithSpan()
        {
            await Task.Yield();
            return HotReloadBclTwinTargets.SpanLength(new ReadOnlySpan<int>(_ints)) + 6051;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureCallsInternalMethodWithSpanLocal()
        {
            Func<int> read = () =>
            {
                Span<int> span = new Span<int>(_ints);
                return HotReloadBclTwinTargets.MutableSpanLength(span) + 6061;
            };
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureCallsInternalMethodWithStackallocSpan()
        {
            Func<int> read = () =>
            {
                Span<int> span = stackalloc int[4];
                return HotReloadBclTwinTargets.MutableSpanLength(span) + 6071;
            };
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureCallsInternalMethodWithAsSpan()
        {
            Func<int> read = () => HotReloadBclTwinTargets.MutableSpanLength(_ints.AsSpan()) + 6081;
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureCallsInternalMethodWithUserRefStruct()
        {
            Func<int> read = () => HotReloadBclTwinTargets.UserRefLength(new HotReloadBclTwinUserRef { X = 2 }) + 6091;
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureCallsInternalMethodWithMemory()
        {
            Func<int> read = () => HotReloadBclTwinTargets.MemoryLength(new Memory<int>(_ints)) + 6101;
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureCallsInternalMethodWithArraySegment()
        {
            Func<int> read = () => HotReloadBclTwinTargets.SegmentLength(new ArraySegment<int>(_ints)) + 6111;
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureCreatesWithInternalConstructorTakingMemory()
        {
            Func<int> read = () => new HotReloadBclTwinBox(new Memory<int>(_ints)).Length + 6121;
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureCreatesWithInternalConstructorTakingArraySegment()
        {
            Func<int> read = () => new HotReloadBclTwinBox(new ArraySegment<int>(_ints)).Length + 6131;
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureCreatesWithInternalConstructorTakingSpan()
        {
            Func<int> read = () => new HotReloadBclTwinBox(new ReadOnlySpan<int>(_ints)).Length + 6141;
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureCreatesWithInternalConstructorTakingUserRefStruct()
        {
            Func<int> read = () => new HotReloadBclTwinBox(new HotReloadBclTwinUserRef { X = 2 }).Length + 6151;
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureReadsPrivateIndexerWithMemory()
        {
            Func<int> read = () => this[new Memory<int>(_ints)] + 6161;
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureReadsPrivateIndexerWithArraySegment()
        {
            Func<int> read = () => this[new ArraySegment<int>(_ints)] + 6171;
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureReadsPrivateIndexerWithSpan()
        {
            Func<int> read = () => this[new ReadOnlySpan<int>(_ints)] + 6181;
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureReadsPrivateIndexerWithUserRefStruct()
        {
            Func<int> read = () => this[new HotReloadBclTwinUserRef { X = 2 }] + 6191;
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int PlainBodyCallsInternalMethodWithSpan()
        {
            return HotReloadBclTwinTargets.SpanLength(new ReadOnlySpan<int>(_ints)) + 6201;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureCallsPublicMethodWithSpan()
        {
            Func<int> read = () => HotReloadBclTwinTargets.PublicSpanLength(new ReadOnlySpan<int>(_ints)) + 6211;
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureCallsInternalMethodWithInterpolatedString()
        {
            Func<int> read = () => HotReloadBclTwinTargets.StringLength($"a{_value}") + 6221;
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureCallsInternalMethodWithIndexFromEnd()
        {
            Func<int> read = () => HotReloadBclTwinTargets.Identity(_ints[^1]) + 6231;
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureCallsInternalMethodOfAnotherFileWithSpan()
        {
            Func<int> read = () => HotReloadBclTwinOtherFileTargets.SpanLength(new ReadOnlySpan<int>(_ints)) + 6241;
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureReadsAndWritesPrivateMemoryField()
        {
            Func<int> read = () =>
            {
                _memory = new Memory<int>(_ints);
                return _memory.Length + 6251;
            };
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureReadsAndWritesPrivateMemoryProperty()
        {
            Func<int> read = () =>
            {
                MemoryProperty = new Memory<int>(_ints);
                return MemoryProperty.Length + 6261;
            };
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureWritesPrivateIndexerWithMemory()
        {
            Action write = () => this[new Memory<int>(_ints)] = 6271;
            write();
            return _stored;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureWritesPrivateIndexerWithArraySegment()
        {
            Action write = () => this[new ArraySegment<int>(_ints)] = 6281;
            write();
            return _stored;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureCompoundWritesPrivateIndexerWithMemory()
        {
            Action write = () => this[new Memory<int>(_ints)] += 6291;
            write();
            return _stored;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureCompoundWritesPrivateIndexerWithArraySegment()
        {
            Action write = () => this[new ArraySegment<int>(_ints)] += 6301;
            write();
            return _stored;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureIncrementsPrivateIndexerWithMemory()
        {
            Action write = () => this[new Memory<int>(_ints)]++;
            write();
            return _stored + 6311;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureIncrementsPrivateIndexerWithArraySegment()
        {
            Action write = () => this[new ArraySegment<int>(_ints)]++;
            write();
            return _stored + 6321;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureInitializesInternalIndexerWithMemory()
        {
            Func<int> read = () => new HotReloadBclTwinIndexed { [new Memory<int>(_ints)] = 6331 }.Stored;
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureInitializesInternalIndexerWithArraySegment()
        {
            Func<int> read = () => new HotReloadBclTwinIndexed { [new ArraySegment<int>(_ints)] = 6341 }.Stored;
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public async Task<int> AsyncCallsInternalMethodWithMemory()
        {
            await Task.Yield();
            return HotReloadBclTwinTargets.MemoryLength(new Memory<int>(_ints)) + 6351;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public async Task<int> AsyncCallsInternalMethodWithArraySegment()
        {
            await Task.Yield();
            return HotReloadBclTwinTargets.SegmentLength(new ArraySegment<int>(_ints)) + 6361;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureCallsInternalParamsMethodWithOneMemory()
        {
            Func<int> read = () => HotReloadBclTwinTargets.ParamsMemoryLength(new Memory<int>(_ints)) + 6371;
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureCallsInternalParamsMethodWithOneArraySegment()
        {
            Func<int> read = () => HotReloadBclTwinTargets.ParamsSegmentLength(new ArraySegment<int>(_ints)) + 6381;
            return read();
        }
    }
}
