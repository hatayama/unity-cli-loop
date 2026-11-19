using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// A public struct no referenced assembly carries a copy of, the oracle for foreach, where
    /// ArraySegment cannot serve because it has a public GetEnumerator of its own.
    /// </summary>
    public struct HotReloadBclTwinPlainValue
    {
        public int[] Items;
    }

    /// <summary>
    /// Collection whose internal Add takes a BCL twin type or its oracle type, filled from a
    /// collection initializer.
    /// </summary>
    public sealed class HotReloadBclTwinBag : IEnumerable<int>
    {
        public int Count;

        internal void Add(Memory<int> memory)
        {
            Count += memory.Length;
        }

        internal void Add(ArraySegment<int> segment)
        {
            Count += segment.Count;
        }

        public IEnumerator<int> GetEnumerator()
        {
            yield return Count;
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }

    /// <summary>
    /// Awaiter that completes at once, returned by the internal GetAwaiter extensions below.
    /// </summary>
    public sealed class HotReloadBclTwinReadyAwaiter : INotifyCompletion
    {
        public int Value;

        public bool IsCompleted => true;

        public int GetResult()
        {
            return Value;
        }

        public void OnCompleted(Action continuation)
        {
            continuation();
        }
    }

    /// <summary>
    /// Public class with internal constructors taking a BCL twin type or its oracle type, declared in
    /// this file so a reload of it is given the constructors' source.
    /// </summary>
    public sealed class HotReloadBclTwinLocalBox
    {
        public int Length;

        internal HotReloadBclTwinLocalBox(Memory<int> memory)
        {
            Length = memory.Length;
        }

        internal HotReloadBclTwinLocalBox(ArraySegment<int> segment)
        {
            Length = segment.Count;
        }
    }

    /// <summary>
    /// Public class with internal indexer setters taking a BCL twin type or its oracle type, declared
    /// in this file, set from an object initializer.
    /// </summary>
    public sealed class HotReloadBclTwinLocalIndexed
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
    /// Internal pattern methods the compiler calls for a BCL twin type or its oracle type
    /// (deconstruction, foreach, await, query).
    /// </summary>
    public static class HotReloadBclTwinPatternExtensions
    {
        internal static void Deconstruct(this Memory<int> memory, out int first, out int length)
        {
            first = memory.Span[0];
            length = memory.Length;
        }

        internal static void Deconstruct(this ArraySegment<int> segment, out int first, out int length)
        {
            first = segment.Array[segment.Offset];
            length = segment.Count;
        }

        internal static IEnumerator<int> GetEnumerator(this Memory<int> memory)
        {
            for (int i = 0; i < memory.Length; i++)
            {
                yield return memory.Span[i];
            }
        }

        internal static IEnumerator<int> GetEnumerator(this HotReloadBclTwinPlainValue value)
        {
            foreach (int item in value.Items)
            {
                yield return item;
            }
        }

        internal static HotReloadBclTwinReadyAwaiter GetAwaiter(this Memory<int> memory)
        {
            return new HotReloadBclTwinReadyAwaiter { Value = memory.Length };
        }

        internal static HotReloadBclTwinReadyAwaiter GetAwaiter(this ArraySegment<int> segment)
        {
            return new HotReloadBclTwinReadyAwaiter { Value = segment.Count };
        }

        internal static int Select(this Memory<int> memory, Func<int, int> selector)
        {
            return selector(memory.Length);
        }

        internal static int Select(this ArraySegment<int> segment, Func<int, int> selector)
        {
            return selector(segment.Count);
        }
    }

    /// <summary>
    /// Host whose lambdas read no private member: the BCL twin type is built from a captured local,
    /// so the only inaccessible access in each body is the one the shape under test makes.
    /// </summary>
    public sealed class HotReloadBclTwinLocalHost
    {
        private int _stored;

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

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureFillsBagWithMemory()
        {
            int[] local = { 1, 2 };
            Func<int> read = () => new HotReloadBclTwinBag { new Memory<int>(local) }.Count + 6501;
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureFillsBagWithArraySegment()
        {
            int[] local = { 1, 2 };
            Func<int> read = () => new HotReloadBclTwinBag { new ArraySegment<int>(local) }.Count + 6511;
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureDeconstructsMemory()
        {
            int[] local = { 1, 2 };
            Func<int> read = () =>
            {
                (int first, int length) = new Memory<int>(local);
                return first + length + 6521;
            };
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureDeconstructsArraySegment()
        {
            int[] local = { 1, 2 };
            Func<int> read = () =>
            {
                (int first, int length) = new ArraySegment<int>(local);
                return first + length + 6531;
            };
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureMatchesMemoryPositionally()
        {
            int[] local = { 1, 2 };
            Func<int> read = () => new Memory<int>(local) is (int first, int length) ? first + length + 6541 : 0;
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureMatchesArraySegmentPositionally()
        {
            int[] local = { 1, 2 };
            Func<int> read = () => new ArraySegment<int>(local) is (int first, int length) ? first + length + 6551 : 0;
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureEnumeratesMemory()
        {
            int[] local = { 1, 2 };
            Func<int> read = () =>
            {
                int sum = 6561;
                foreach (int item in new Memory<int>(local))
                {
                    sum += item;
                }

                return sum;
            };
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureEnumeratesPlainValue()
        {
            int[] local = { 1, 2 };
            Func<int> read = () =>
            {
                int sum = 6571;
                foreach (int item in new HotReloadBclTwinPlainValue { Items = local })
                {
                    sum += item;
                }

                return sum;
            };
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureAwaitsMemory()
        {
            int[] local = { 1, 2 };
            Func<Task<int>> read = async () => await new Memory<int>(local) + 6581;
            return read().Result;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureAwaitsArraySegment()
        {
            int[] local = { 1, 2 };
            Func<Task<int>> read = async () => await new ArraySegment<int>(local) + 6591;
            return read().Result;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureQueriesMemory()
        {
            int[] local = { 1, 2 };
            Func<int> read = () => (from item in new Memory<int>(local) select item + 6601);
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureQueriesArraySegment()
        {
            int[] local = { 1, 2 };
            Func<int> read = () => (from item in new ArraySegment<int>(local) select item + 6611);
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureCreatesBoxFromLocalMemory()
        {
            int[] local = { 1, 2 };
            Func<int> read = () => new HotReloadBclTwinLocalBox(new Memory<int>(local)).Length + 6641;
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureCreatesBoxFromLocalArraySegment()
        {
            int[] local = { 1, 2 };
            Func<int> read = () => new HotReloadBclTwinLocalBox(new ArraySegment<int>(local)).Length + 6651;
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureReadsPrivateIndexerWithLocalMemory()
        {
            int[] local = { 1, 2 };
            Func<int> read = () => this[new Memory<int>(local)] + 6661;
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureReadsPrivateIndexerWithLocalArraySegment()
        {
            int[] local = { 1, 2 };
            Func<int> read = () => this[new ArraySegment<int>(local)] + 6671;
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureWritesPrivateIndexerWithLocalMemory()
        {
            int[] local = { 1, 2 };
            Action write = () => this[new Memory<int>(local)] = 6681;
            write();
            return _stored;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureWritesPrivateIndexerWithLocalArraySegment()
        {
            int[] local = { 1, 2 };
            Action write = () => this[new ArraySegment<int>(local)] = 6691;
            write();
            return _stored;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureCompoundWritesPrivateIndexerWithLocalMemory()
        {
            int[] local = { 1, 2 };
            Action write = () => this[new Memory<int>(local)] += 6701;
            write();
            return _stored;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureCompoundWritesPrivateIndexerWithLocalArraySegment()
        {
            int[] local = { 1, 2 };
            Action write = () => this[new ArraySegment<int>(local)] += 6711;
            write();
            return _stored;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureIncrementsPrivateIndexerWithLocalMemory()
        {
            int[] local = { 1, 2 };
            Action write = () => this[new Memory<int>(local)]++;
            write();
            return _stored + 6721;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureIncrementsPrivateIndexerWithLocalArraySegment()
        {
            int[] local = { 1, 2 };
            Action write = () => this[new ArraySegment<int>(local)]++;
            write();
            return _stored + 6731;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureInitializesInternalIndexerWithLocalMemory()
        {
            int[] local = { 1, 2 };
            Func<int> read = () => new HotReloadBclTwinLocalIndexed { [new Memory<int>(local)] = 6741 }.Stored;
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureInitializesInternalIndexerWithLocalArraySegment()
        {
            int[] local = { 1, 2 };
            Func<int> read = () => new HotReloadBclTwinLocalIndexed { [new ArraySegment<int>(local)] = 6751 }.Stored;
            return read();
        }
    }
}
