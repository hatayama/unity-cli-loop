using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Internal extension methods the compiler calls for await, foreach, deconstruction, collection
    /// initializers, and query expressions, with no invocation of them in the source. The class is
    /// public so the methods are not offered twice to the transform worker.
    /// </summary>
    public static class HotReloadImplicitCallExtensions
    {
        internal static HotReloadImplicitAwaiter GetAwaiter(this HotReloadImplicitAwaitable awaitable)
        {
            return new HotReloadImplicitAwaiter(awaitable.Value);
        }

        internal static HotReloadImplicitEnumerator GetEnumerator(this HotReloadImplicitRange range)
        {
            return new HotReloadImplicitEnumerator(range.Count);
        }

        internal static void Deconstruct(this HotReloadImplicitExtensionPair pair, out int first, out int second)
        {
            first = pair.First;
            second = pair.Second;
        }

        internal static void Add(this HotReloadImplicitBag bag, int value)
        {
            bag.Sum += value;
        }

        internal static HotReloadImplicitSource Select(this HotReloadImplicitSource source, Func<int, int> selector)
        {
            return new HotReloadImplicitSource { Value = selector(source.Value) };
        }

        internal static HotReloadImplicitFilterSource Where(
            this HotReloadImplicitFilterSource source,
            Func<int, bool> predicate)
        {
            return new HotReloadImplicitFilterSource { Value = predicate(source.Value) ? source.Value : 0 };
        }

        internal static HotReloadImplicitAsyncEnumerator GetAsyncEnumerator(this HotReloadImplicitAsyncSequence sequence)
        {
            return new HotReloadImplicitAsyncEnumerator(sequence.Count);
        }

        internal static HotReloadImplicitOrderedSource OrderBy(
            this HotReloadImplicitOrderedSource source,
            Func<int, int> keySelector)
        {
            return new HotReloadImplicitOrderedSource { Value = keySelector(source.Value) };
        }

        internal static HotReloadImplicitCastResult Cast<T>(this HotReloadImplicitCastSource source)
        {
            return new HotReloadImplicitCastResult { Value = source.Value };
        }
    }

    /// <summary>
    /// Awaitable whose awaiter comes from an internal extension method.
    /// </summary>
    public sealed class HotReloadImplicitAwaitable
    {
        public int Value = 10;
    }

    /// <summary>
    /// Completed awaiter with public members.
    /// </summary>
    public readonly struct HotReloadImplicitAwaiter : INotifyCompletion
    {
        private readonly int _value;

        public HotReloadImplicitAwaiter(int value)
        {
            _value = value;
        }

        public bool IsCompleted => true;

        public int GetResult()
        {
            return _value;
        }

        public void OnCompleted(Action continuation)
        {
            continuation();
        }
    }

    /// <summary>
    /// Awaitable with a public GetAwaiter whose awaiter has an internal GetResult.
    /// </summary>
    public sealed class HotReloadImplicitInternalResultAwaitable
    {
        public int Value = 20;

        public HotReloadImplicitInternalResultAwaiter GetAwaiter()
        {
            return new HotReloadImplicitInternalResultAwaiter(Value);
        }
    }

    /// <summary>
    /// Completed awaiter whose GetResult is internal.
    /// </summary>
    public readonly struct HotReloadImplicitInternalResultAwaiter : INotifyCompletion
    {
        private readonly int _value;

        public HotReloadImplicitInternalResultAwaiter(int value)
        {
            _value = value;
        }

        public bool IsCompleted => true;

        internal int GetResult()
        {
            return _value;
        }

        public void OnCompleted(Action continuation)
        {
            continuation();
        }
    }

    /// <summary>
    /// Range enumerated through an internal extension GetEnumerator.
    /// </summary>
    public sealed class HotReloadImplicitRange
    {
        public int Count = 3;
    }

    /// <summary>
    /// Enumerator with public members, counting 1..count.
    /// </summary>
    public struct HotReloadImplicitEnumerator
    {
        private readonly int _count;
        private int _index;

        public HotReloadImplicitEnumerator(int count)
        {
            _count = count;
            _index = 0;
        }

        public int Current => _index;

        public bool MoveNext()
        {
            _index++;
            return _index <= _count;
        }
    }

    /// <summary>
    /// Pair deconstructed through an internal extension Deconstruct.
    /// </summary>
    public sealed class HotReloadImplicitExtensionPair
    {
        public int First = 20;
        public int Second = 30;
    }

    /// <summary>
    /// Pair with an internal instance Deconstruct.
    /// </summary>
    public sealed class HotReloadImplicitInternalPair
    {
        public int First = 40;
        public int Second = 50;

        internal void Deconstruct(out int first, out int second)
        {
            first = First;
            second = Second;
        }
    }

    /// <summary>
    /// Outer value with a public Deconstruct whose first element has an internal Deconstruct.
    /// </summary>
    public sealed class HotReloadImplicitOuterPair
    {
        public HotReloadImplicitInternalPair Inner = new HotReloadImplicitInternalPair();
        public int Third = 60;

        public void Deconstruct(out HotReloadImplicitInternalPair inner, out int third)
        {
            inner = Inner;
            third = Third;
        }
    }

    /// <summary>
    /// Pair with a public Deconstruct, the accessible control.
    /// </summary>
    public sealed class HotReloadImplicitPublicPair
    {
        public int First = 70;
        public int Second = 80;

        public void Deconstruct(out int first, out int second)
        {
            first = First;
            second = Second;
        }
    }

    /// <summary>
    /// Collection filled through an internal extension Add.
    /// </summary>
    public sealed class HotReloadImplicitBag : IEnumerable
    {
        public int Sum = 100;

        public IEnumerator GetEnumerator()
        {
            return Array.Empty<int>().GetEnumerator();
        }
    }

    /// <summary>
    /// Collection with an internal instance Add.
    /// </summary>
    public sealed class HotReloadImplicitInternalAddBag : IEnumerable
    {
        public int Sum = 200;

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
    /// Query source whose Select is an internal extension method.
    /// </summary>
    public sealed class HotReloadImplicitSource
    {
        public int Value = 7;
    }

    /// <summary>
    /// Query source whose only query method is an internal extension Where.
    /// </summary>
    public sealed class HotReloadImplicitFilterSource
    {
        public int Value = 9;
    }

    /// <summary>
    /// Ref struct resource whose pattern Dispose is internal.
    /// </summary>
    public ref struct HotReloadImplicitRefResource
    {
        internal void Dispose()
        {
        }
    }

    /// <summary>
    /// Async sequence enumerated through an internal extension GetAsyncEnumerator.
    /// </summary>
    public sealed class HotReloadImplicitAsyncSequence
    {
        public int Count = 2;
    }

    /// <summary>
    /// Async enumerator with public members, counting 1..count.
    /// </summary>
    public sealed class HotReloadImplicitAsyncEnumerator
    {
        private readonly int _count;
        private int _index;

        public HotReloadImplicitAsyncEnumerator(int count)
        {
            _count = count;
        }

        public int Current => _index;

        public ValueTask<bool> MoveNextAsync()
        {
            _index++;
            return new ValueTask<bool>(_index <= _count);
        }
    }

    /// <summary>
    /// Resource without IAsyncDisposable whose pattern DisposeAsync is internal.
    /// </summary>
    public sealed class HotReloadImplicitAsyncResource
    {
        internal ValueTask DisposeAsync()
        {
            return default;
        }
    }

    /// <summary>
    /// Resource that implements IAsyncDisposable and also has an internal parameterless
    /// DisposeAsync; the Unity compiler's await using calls the interface method, not the pattern.
    /// </summary>
    public sealed class HotReloadImplicitInterfaceAndPatternResource : IAsyncDisposable
    {
        internal ValueTask DisposeAsync()
        {
            return default;
        }

        ValueTask IAsyncDisposable.DisposeAsync()
        {
            return default;
        }
    }

    /// <summary>
    /// Resource whose internal pattern DisposeAsync takes only an optional parameter.
    /// </summary>
    public sealed class HotReloadImplicitOptionalParameterResource
    {
        internal ValueTask DisposeAsync(int delay = 0)
        {
            return default;
        }
    }

    /// <summary>
    /// Resource whose public DisposeAsync with an optional parameter is declared before an internal
    /// parameterless one; the compiler binds the parameterless overload.
    /// </summary>
    public sealed class HotReloadImplicitCompetingDisposeAsyncResource
    {
        public ValueTask DisposeAsync(int delay = 0)
        {
            return default;
        }

        internal ValueTask DisposeAsync()
        {
            return default;
        }
    }

    /// <summary>
    /// Resource whose internal params DisposeAsync is declared before a public one with an optional
    /// parameter; the compiler binds the public one, applicable in its normal form.
    /// </summary>
    public sealed class HotReloadImplicitParamsAndOptionalDisposeAsyncResource
    {
        internal ValueTask DisposeAsync(params int[] values)
        {
            return default;
        }

        public ValueTask DisposeAsync(int delay = 0)
        {
            return default;
        }
    }

    /// <summary>
    /// Resource with a public generic DisposeAsync, which a call without arguments cannot bind, and
    /// an internal one with an optional parameter, which the compiler binds.
    /// </summary>
    public sealed class HotReloadImplicitGenericAndOptionalDisposeAsyncResource
    {
        public ValueTask DisposeAsync<T>()
        {
            return default;
        }

        internal ValueTask DisposeAsync(int delay = 0)
        {
            return default;
        }
    }

    /// <summary>
    /// Resource with an internal DisposeAsync taking two optional parameters and a public params
    /// one; the compiler binds the internal one, applicable in its normal form.
    /// </summary>
    public sealed class HotReloadImplicitOptionalAndParamsDisposeAsyncResource
    {
        internal ValueTask DisposeAsync(int first = 0, int second = 0)
        {
            return default;
        }

        public ValueTask DisposeAsync(params int[] values)
        {
            return default;
        }
    }

    /// <summary>
    /// Base resource whose pattern DisposeAsync is internal, inherited by the resource below.
    /// </summary>
    public class HotReloadImplicitAsyncResourceBase
    {
        internal ValueTask DisposeAsync()
        {
            return default;
        }
    }

    /// <summary>
    /// Resource without IAsyncDisposable that inherits an internal pattern DisposeAsync.
    /// </summary>
    public sealed class HotReloadImplicitDerivedAsyncResource : HotReloadImplicitAsyncResourceBase
    {
    }

    /// <summary>
    /// Query source whose only query method is an internal extension OrderBy.
    /// </summary>
    public sealed class HotReloadImplicitOrderedSource
    {
        public int Value = 4;
    }

    /// <summary>
    /// Query source that a typed range variable converts through an internal extension Cast.
    /// </summary>
    public sealed class HotReloadImplicitCastSource
    {
        public int Value = 6;
    }

    /// <summary>
    /// Result of the internal Cast, with a public Select so only the Cast is inaccessible.
    /// </summary>
    public sealed class HotReloadImplicitCastResult
    {
        public int Value;

        public HotReloadImplicitCastResult Select(Func<int, int> selector)
        {
            return new HotReloadImplicitCastResult { Value = selector(Value) };
        }
    }

    /// <summary>
    /// Query source with a public Join whose inner sequence is the result of the internal Cast.
    /// </summary>
    public sealed class HotReloadImplicitJoinSource
    {
        public int Value = 6;

        public HotReloadImplicitJoinSource Join(
            HotReloadImplicitCastResult inner,
            Func<int, int> outerKeySelector,
            Func<int, int> innerKeySelector,
            Func<int, int, int> resultSelector)
        {
            int result = outerKeySelector(Value) == innerKeySelector(inner.Value)
                ? resultSelector(Value, inner.Value)
                : 0;
            return new HotReloadImplicitJoinSource { Value = result };
        }
    }

    /// <summary>
    /// Compiled host whose lambdas and async methods reach internal methods only through calls
    /// the compiler generates. Each body reads a private field and holds a 4-digit token unique in
    /// this file, which the tests edit.
    /// </summary>
    public sealed class HotReloadImplicitCallHost
    {
        private HotReloadImplicitAwaitable _awaitable = new HotReloadImplicitAwaitable();
        private HotReloadImplicitInternalResultAwaitable _internalResultAwaitable =
            new HotReloadImplicitInternalResultAwaitable();
        private HotReloadImplicitRange _range = new HotReloadImplicitRange();
        private HotReloadImplicitExtensionPair _extensionPair = new HotReloadImplicitExtensionPair();
        private HotReloadImplicitInternalPair _internalPair = new HotReloadImplicitInternalPair();
        private HotReloadImplicitOuterPair _outerPair = new HotReloadImplicitOuterPair();
        private HotReloadImplicitPublicPair _publicPair = new HotReloadImplicitPublicPair();
        private HotReloadImplicitSource _source = new HotReloadImplicitSource();
        private HotReloadImplicitFilterSource _filterSource = new HotReloadImplicitFilterSource();
        private HotReloadImplicitAsyncSequence _asyncSequence = new HotReloadImplicitAsyncSequence();
        private List<int> _list = new List<int> { 1, 2 };
        private int _seed = 5;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public Task<int> ClosureAwaitsThroughInternalGetAwaiter()
        {
            Func<Task<int>> read = async () => await _awaitable + 6101;
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public async Task<int> AsyncAwaitsThroughInternalGetAwaiter()
        {
            await Task.Yield();
            return await _awaitable + 6111;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public Task<int> ClosureAwaitsThroughInternalGetResult()
        {
            Func<Task<int>> read = async () => await _internalResultAwaitable + 6121;
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureLoopsThroughInternalGetEnumerator()
        {
            Func<int> read = () =>
            {
                int sum = 6201;
                foreach (int value in _range)
                {
                    sum += value;
                }

                return sum;
            };
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public async Task<int> AsyncLoopsThroughInternalGetEnumerator()
        {
            await Task.Yield();
            int sum = 6211;
            foreach (int value in _range)
            {
                sum += value;
            }

            return sum;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureDeconstructsThroughInternalExtension()
        {
            Func<int> read = () =>
            {
                (int first, int second) = _extensionPair;
                return first + second + 6301;
            };
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureDeconstructsThroughInternalInstanceMethod()
        {
            Func<int> read = () =>
            {
                (int first, int second) = _internalPair;
                return first + second + 6311;
            };
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public async Task<int> AsyncDeconstructsThroughInternalInstanceMethod()
        {
            await Task.Yield();
            (int first, int second) = _internalPair;
            return first + second + 6321;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureDeconstructsNestedThroughInternalInstanceMethod()
        {
            Func<int> read = () =>
            {
                ((int first, int second), int third) = _outerPair;
                return first + second + third + 6331;
            };
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureFillsThroughInternalExtensionAdd()
        {
            Func<int> read = () =>
            {
                HotReloadImplicitBag bag = new HotReloadImplicitBag { _seed, 6401 };
                return bag.Sum;
            };
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureFillsThroughInternalInstanceAdd()
        {
            Func<int> read = () =>
            {
                HotReloadImplicitInternalAddBag bag = new HotReloadImplicitInternalAddBag { _seed, 6411 };
                return bag.Sum;
            };
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public async Task<int> AsyncFillsThroughInternalExtensionAdd()
        {
            await Task.Yield();
            HotReloadImplicitBag bag = new HotReloadImplicitBag { _seed, 6421 };
            return bag.Sum;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureQueriesThroughInternalSelect()
        {
            Func<int> read = () =>
            {
                HotReloadImplicitSource result = from value in _source select value + 6501;
                return result.Value;
            };
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public async Task<int> AsyncQueriesThroughInternalSelect()
        {
            await Task.Yield();
            HotReloadImplicitSource result = from value in _source select value + 6511;
            return result.Value;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureQueriesThroughInternalWhere()
        {
            Func<int> read = () =>
            {
                HotReloadImplicitFilterSource result = from value in _filterSource where value < 6521 select value;
                return result.Value;
            };
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureUsesRefStructWithInternalDispose()
        {
            Func<int> read = () =>
            {
                int result = _seed + 6601;
                using (HotReloadImplicitRefResource resource = new HotReloadImplicitRefResource())
                {
                    result += 1;
                }

                return result;
            };
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public Task<int> ClosureAwaitForEachesThroughInternalGetAsyncEnumerator()
        {
            Func<Task<int>> read = async () =>
            {
                int sum = 6701;
                await foreach (int value in _asyncSequence)
                {
                    sum += value;
                }

                return sum;
            };
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public async Task<int> AsyncAwaitForEachesThroughInternalGetAsyncEnumerator()
        {
            await Task.Yield();
            int sum = 6711;
            await foreach (int value in _asyncSequence)
            {
                sum += value;
            }

            return sum;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureDeconstructsThroughPublicMethod()
        {
            Func<int> read = () =>
            {
                (int first, int second) = _publicPair;
                return first + second + _seed + 6801;
            };
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureFillsPublicList()
        {
            Func<int> read = () =>
            {
                List<int> values = new List<int> { _seed, 6811 };
                return values.Sum();
            };
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureQueriesPublicList()
        {
            Func<int> read = () => (from value in _list select value + 6821).Sum();
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public Task<int> ClosureAwaitsPublicTask()
        {
            Func<Task<int>> read = async () => await Task.FromResult(_seed) + 6831;
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureLoopsOverPublicList()
        {
            Func<int> read = () =>
            {
                int sum = 6841;
                foreach (int value in _list)
                {
                    sum += value;
                }

                return sum;
            };
            return read();
        }

        // The bodies below touch no private member: the only inaccessible use is the call the
        // compiler generates, so the body reaches the accessor decision through that call alone.

        [MethodImpl(MethodImplOptions.NoInlining)]
        public Task<int> ClosureOnlyAwaitsThroughInternalGetAwaiter()
        {
            Func<Task<int>> read = async () => await new HotReloadImplicitAwaitable() + 9101;
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public Task<int> ClosureOnlyAwaitsThroughInternalGetResult()
        {
            Func<Task<int>> read = async () => await new HotReloadImplicitInternalResultAwaitable() + 9111;
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureOnlyLoopsThroughInternalGetEnumerator()
        {
            Func<int> read = () =>
            {
                int sum = 9121;
                foreach (int value in new HotReloadImplicitRange())
                {
                    sum += value;
                }

                return sum;
            };
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public async Task<int> AsyncOnlyLoopsThroughInternalGetEnumerator()
        {
            await Task.Yield();
            int sum = 9126;
            foreach (int value in new HotReloadImplicitRange())
            {
                sum += value;
            }

            return sum;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureOnlyDeconstructsThroughInternalExtension()
        {
            Func<int> read = () =>
            {
                (int first, int second) = new HotReloadImplicitExtensionPair();
                return first + second + 9131;
            };
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureOnlyDeconstructsThroughInternalInstanceMethod()
        {
            Func<int> read = () =>
            {
                (int first, int second) = new HotReloadImplicitInternalPair();
                return first + second + 9141;
            };
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureOnlyFillsThroughInternalExtensionAdd()
        {
            Func<int> read = () => new HotReloadImplicitBag { 9151 }.Sum;
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureOnlyFillsThroughInternalInstanceAdd()
        {
            Func<int> read = () => new HotReloadImplicitInternalAddBag { 9161 }.Sum;
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureOnlyQueriesThroughInternalSelect()
        {
            Func<int> read = () => (from value in new HotReloadImplicitSource() select value + 9171).Value;
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureOnlyUsesRefStructWithInternalDispose()
        {
            Func<int> read = () =>
            {
                int result = 9181;
                using (HotReloadImplicitRefResource resource = new HotReloadImplicitRefResource())
                {
                    result += 1;
                }

                return result;
            };
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public Task<int> ClosureOnlyAwaitForEachesThroughInternalGetAsyncEnumerator()
        {
            Func<Task<int>> read = async () =>
            {
                int sum = 9191;
                await foreach (int value in new HotReloadImplicitAsyncSequence())
                {
                    sum += value;
                }

                return sum;
            };
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public Task<int> ClosureAwaitUsesInternalDisposeAsync()
        {
            Func<Task<int>> read = async () =>
            {
                int result = 9301;
                await using (HotReloadImplicitAsyncResource resource = new HotReloadImplicitAsyncResource())
                {
                    result += 1;
                }

                return result;
            };
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public async Task<int> AsyncAwaitUsingDeclarationWithInternalDisposeAsync()
        {
            await Task.Yield();
            await using HotReloadImplicitAsyncResource resource = new HotReloadImplicitAsyncResource();
            return 9311;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public Task<int> ClosureAwaitUsesInheritedInternalDisposeAsync()
        {
            Func<Task<int>> read = async () =>
            {
                int result = 9431;
                await using (HotReloadImplicitDerivedAsyncResource resource = new HotReloadImplicitDerivedAsyncResource())
                {
                    result += 1;
                }

                return result;
            };
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public Task<int> ClosureAwaitUsesInterfaceDisposeAsyncBesideInternalPattern()
        {
            Func<Task<int>> read = async () =>
            {
                int result = 9441;
                await using (HotReloadImplicitInterfaceAndPatternResource resource = new HotReloadImplicitInterfaceAndPatternResource())
                {
                    result += 1;
                }

                return result;
            };
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public Task<int> ClosureAwaitUsesOptionalParameterDisposeAsync()
        {
            Func<Task<int>> read = async () =>
            {
                int result = 9451;
                await using (HotReloadImplicitOptionalParameterResource resource = new HotReloadImplicitOptionalParameterResource())
                {
                    result += 1;
                }

                return result;
            };
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public Task<int> ClosureAwaitUsesParameterlessDisposeAsyncOverOptionalOverload()
        {
            Func<Task<int>> read = async () =>
            {
                int result = 9461;
                await using (HotReloadImplicitCompetingDisposeAsyncResource resource = new HotReloadImplicitCompetingDisposeAsyncResource())
                {
                    result += 1;
                }

                return result;
            };
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public Task<int> ClosureAwaitUsesOptionalDisposeAsyncOverParamsOverload()
        {
            Func<Task<int>> read = async () =>
            {
                int result = 9471;
                await using (HotReloadImplicitParamsAndOptionalDisposeAsyncResource resource = new HotReloadImplicitParamsAndOptionalDisposeAsyncResource())
                {
                    result += 1;
                }

                return result;
            };
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public Task<int> ClosureAwaitUsesOptionalDisposeAsyncBesideGenericOverload()
        {
            Func<Task<int>> read = async () =>
            {
                int result = 9481;
                await using (HotReloadImplicitGenericAndOptionalDisposeAsyncResource resource = new HotReloadImplicitGenericAndOptionalDisposeAsyncResource())
                {
                    result += 1;
                }

                return result;
            };
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public Task<int> ClosureAwaitUsesTwoOptionalDisposeAsyncOverParamsOverload()
        {
            Func<Task<int>> read = async () =>
            {
                int result = 9491;
                await using (HotReloadImplicitOptionalAndParamsDisposeAsyncResource resource = new HotReloadImplicitOptionalAndParamsDisposeAsyncResource())
                {
                    result += 1;
                }

                return result;
            };
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureMatchesIsPositionalPatternThroughInternalDeconstruct()
        {
            Func<int> read = () => new HotReloadImplicitInternalPair() is (int first, int second)
                ? first + second + 9321
                : 0;
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureMatchesSwitchArmPositionalPatternThroughInternalDeconstruct()
        {
            Func<int> read = () => new HotReloadImplicitInternalPair() switch
            {
                (int first, int second) => first + second + 9331,
                _ => 0,
            };
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureMatchesCasePositionalPatternThroughInternalDeconstruct()
        {
            Func<int> read = () =>
            {
                object pair = new HotReloadImplicitInternalPair();
                switch (pair)
                {
                    case HotReloadImplicitInternalPair(int first, int second):
                        return first + second + 9341;
                    default:
                        return 0;
                }
            };
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureOrdersThroughInternalOrderBy()
        {
            Func<int> read = () =>
                (from value in new HotReloadImplicitOrderedSource() orderby value + 9351 select value).Value;
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureQueriesTypedFromThroughInternalCast()
        {
            Func<int> read = () => (from int value in new HotReloadImplicitCastSource() select value + 9361).Value;
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureQueriesTypedJoinThroughInternalCast()
        {
            Func<int> read = () =>
                (from value in new HotReloadImplicitJoinSource()
                 join int other in new HotReloadImplicitCastSource() on value equals other
                 select value + other + 9371).Value;
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureLoopsWithDeconstructionThroughInternalDeconstruct()
        {
            Func<int> read = () =>
            {
                int sum = 9381;
                foreach ((int first, int second) in new[] { new HotReloadImplicitInternalPair() })
                {
                    sum += first + second;
                }

                return sum;
            };
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureUsesVarDeclarationOfRefStructWithInternalDispose()
        {
            Func<int> read = () =>
            {
                // Why var: the resource type of a using var declaration comes from the initializer,
                // which is the shape under test.
                using var resource = new HotReloadImplicitRefResource();
                return 9401;
            };
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureUsesExpressionOfRefStructWithInternalDispose()
        {
            Func<int> read = () =>
            {
                int result = 9411;
                using (new HotReloadImplicitRefResource())
                {
                    result += 1;
                }

                return result;
            };
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureUsesDeclarationOfRefStructWithInternalDispose()
        {
            Func<int> read = () =>
            {
                using HotReloadImplicitRefResource resource = new HotReloadImplicitRefResource();
                return 9421;
            };
            return read();
        }
    }
}
