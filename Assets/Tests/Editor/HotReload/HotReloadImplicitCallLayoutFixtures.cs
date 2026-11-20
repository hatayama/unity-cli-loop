using System;
using System.Collections;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Internal class whose extension methods are public, declared in the same file as the host,
    /// so the transform worker is offered each method twice (the edited source and the compiled
    /// assembly) and binds the implicit call ambiguously.
    /// </summary>
    internal static class HotReloadTwinImplicitCallExtensions
    {
        public static HotReloadImplicitAwaiter GetAwaiter(this HotReloadTwinAwaitable awaitable)
        {
            return new HotReloadImplicitAwaiter(awaitable.Value);
        }

        public static HotReloadImplicitEnumerator GetEnumerator(this HotReloadTwinRange range)
        {
            return new HotReloadImplicitEnumerator(range.Count);
        }

        public static void Deconstruct(this HotReloadTwinPair pair, out int first, out int second)
        {
            first = pair.First;
            second = pair.Second;
        }

        public static void Add(this HotReloadTwinBag bag, int value)
        {
            bag.Sum += value;
        }

        public static HotReloadTwinSource Select(this HotReloadTwinSource source, Func<int, int> selector)
        {
            return new HotReloadTwinSource { Value = selector(source.Value) };
        }
    }

    /// <summary>
    /// Awaitable whose awaiter comes from an extension method of an internal class.
    /// </summary>
    public sealed class HotReloadTwinAwaitable
    {
        public int Value = 12;
    }

    /// <summary>
    /// Range enumerated through an extension GetEnumerator of an internal class.
    /// </summary>
    public sealed class HotReloadTwinRange
    {
        public int Count = 3;
    }

    /// <summary>
    /// Pair deconstructed through an extension Deconstruct of an internal class.
    /// </summary>
    public sealed class HotReloadTwinPair
    {
        public int First = 22;
        public int Second = 32;
    }

    /// <summary>
    /// Collection filled through an extension Add of an internal class.
    /// </summary>
    public sealed class HotReloadTwinBag : IEnumerable
    {
        public int Sum = 102;

        public IEnumerator GetEnumerator()
        {
            return Array.Empty<int>().GetEnumerator();
        }
    }

    /// <summary>
    /// Query source whose Select is an extension method of an internal class.
    /// </summary>
    public sealed class HotReloadTwinSource
    {
        public int Value = 9;
    }

    /// <summary>
    /// Partial pair whose internal Deconstruct is declared in its other part, in a file the run
    /// does not pass.
    /// </summary>
    public sealed partial class HotReloadSplitPair
    {
        public int First = 23;
        public int Second = 33;
    }

    /// <summary>
    /// Compiled host whose lambdas reach an internal method only through a call the compiler
    /// generates, touching no private member: the methods live either in a file the run does not
    /// pass or in an internal class of this file. Each body holds a 4-digit token unique in this
    /// file, which the tests edit.
    /// </summary>
    public sealed class HotReloadImplicitCallLayoutHost
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public Task<int> ClosureAwaitsThroughUnpassedGetAwaiter()
        {
            Func<Task<int>> read = async () => await new HotReloadUnpassedAwaitable() + 8101;
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureLoopsThroughUnpassedGetEnumerator()
        {
            Func<int> read = () =>
            {
                int sum = 8111;
                foreach (int value in new HotReloadUnpassedRange())
                {
                    sum += value;
                }

                return sum;
            };
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureDeconstructsThroughUnpassedExtension()
        {
            Func<int> read = () =>
            {
                (int first, int second) = new HotReloadUnpassedExtensionPair();
                return first + second + 8121;
            };
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureDeconstructsThroughUnpassedInstanceMethod()
        {
            Func<int> read = () =>
            {
                (int first, int second) = new HotReloadUnpassedInternalPair();
                return first + second + 8131;
            };
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureFillsThroughUnpassedExtensionAdd()
        {
            Func<int> read = () => new HotReloadUnpassedBag { 8141 }.Sum;
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureFillsThroughUnpassedInstanceAdd()
        {
            Func<int> read = () => new HotReloadUnpassedInternalAddBag { 8151 }.Sum;
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureQueriesThroughUnpassedSelect()
        {
            Func<int> read = () => (from value in new HotReloadUnpassedSource() select value + 8161).Value;
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureUsesUnpassedRefStructWithInternalDispose()
        {
            Func<int> read = () =>
            {
                int result = 8171;
                using (HotReloadUnpassedRefResource resource = new HotReloadUnpassedRefResource())
                {
                    result += 1;
                }

                return result;
            };
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public Task<int> ClosureAwaitUsesUnpassedInternalDisposeAsync()
        {
            Func<Task<int>> read = async () =>
            {
                int result = 8181;
                await using (HotReloadUnpassedAsyncResource resource = new HotReloadUnpassedAsyncResource())
                {
                    result += 1;
                }

                return result;
            };
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public async Task<int> AsyncLoopsThroughUnpassedGetEnumerator()
        {
            await Task.Yield();
            int sum = 8301;
            foreach (int value in new HotReloadUnpassedRange())
            {
                sum += value;
            }

            return sum;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public async Task<int> AsyncDeconstructsThroughUnpassedInstanceMethod()
        {
            await Task.Yield();
            (int first, int second) = new HotReloadUnpassedInternalPair();
            return first + second + 8311;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int OwnStatementsLoopThroughUnpassedGetEnumerator()
        {
            int sum = 8321;
            foreach (int value in new HotReloadUnpassedRange())
            {
                sum += value;
            }

            return sum;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureQueriesThroughUnpassedWhere()
        {
            Func<int> read = () => (from value in new HotReloadUnpassedFilterSource() where value < 8331 select value).Value;
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureMatchesPositionalPatternThroughUnpassedDeconstruct()
        {
            Func<int> read = () => new HotReloadUnpassedInternalPair() is (int first, int second)
                ? first + second + 8341
                : 0;
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureDeconstructsNestedThroughUnpassedInstanceMethod()
        {
            Func<int> read = () =>
            {
                ((int first, int second), int third) = (new HotReloadUnpassedInternalPair(), 8381);
                return first + second + third;
            };
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureDeconstructsNestedThroughPublicOuterDeconstruct()
        {
            Func<int> read = () =>
            {
                ((int first, int second), int extra) = new HotReloadUnpassedOuterPair();
                return first + second + extra + 8391;
            };
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public Task<int> ClosureAwaitsThroughUnpassedGetResult()
        {
            Func<Task<int>> read = async () => await new HotReloadUnpassedInternalResultAwaitable() + 8351;
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureQueriesThroughUnpassedSelectAfterPublicWhere()
        {
            Func<int> read = () =>
                (from value in new HotReloadUnpassedChainSource() where value > 0 select value + 8361).Value;
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureDeconstructsThroughDeconstructOfOtherPartialPart()
        {
            Func<int> read = () =>
            {
                (int first, int second) = new HotReloadSplitPair();
                return first + second + 8371;
            };
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public Task<int> ClosureAwaitsThroughTwinGetAwaiter()
        {
            Func<Task<int>> read = async () => await new HotReloadTwinAwaitable() + 8201;
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureLoopsThroughTwinGetEnumerator()
        {
            Func<int> read = () =>
            {
                int sum = 8211;
                foreach (int value in new HotReloadTwinRange())
                {
                    sum += value;
                }

                return sum;
            };
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureDeconstructsThroughTwinExtension()
        {
            Func<int> read = () =>
            {
                (int first, int second) = new HotReloadTwinPair();
                return first + second + 8221;
            };
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureFillsThroughTwinExtensionAdd()
        {
            Func<int> read = () => new HotReloadTwinBag { 8231 }.Sum;
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureQueriesThroughTwinSelect()
        {
            Func<int> read = () => (from value in new HotReloadTwinSource() select value + 8241).Value;
            return read();
        }
    }
}
