using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using static io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload.HotReloadNonPublicClassExtensions;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Internal interface a public class implements, so a receiver of that public class reaches an
    /// extension method whose receiver type is not visible from another assembly.
    /// </summary>
    internal interface IHotReloadNonPublicMarker
    {
    }

    /// <summary>
    /// Public class that implements <see cref="IHotReloadNonPublicMarker"/>.
    /// </summary>
    public sealed class HotReloadNonPublicMarked : IHotReloadNonPublicMarker
    {
    }

    /// <summary>
    /// Internal static class whose extension methods the host below calls, declared in the same
    /// file as the host. An accessor delegate cannot name the class, so a lambda or async body that
    /// calls one needs an accessor bound to the class by name.
    /// </summary>
    internal static class HotReloadNonPublicClassExtensions
    {
        internal static int NonPublicTriple(this int value, int add)
        {
            return value * 3 + add;
        }

        internal static int NonPublicPlusOne(this int value, int add)
        {
            return value + add + 1;
        }

        // Public, yet out of reach from another assembly, because its class is internal.
        public static int NonPublicDouble(this int value, int add)
        {
            return value * 2 + add;
        }

        // The condition symbol is never defined, so the compiler omits every call to this one.
        [Conditional("ULOOP_HOT_RELOAD_TEST_UNDEFINED_SYMBOL")]
        internal static void NonPublicTrace(this int value, int add)
        {
            throw new InvalidOperationException("A [Conditional] call must be omitted.");
        }

        internal static ReadOnlySpan<int> NonPublicSpanOf(this int[] values)
        {
            return new ReadOnlySpan<int>(values);
        }

        internal static int NonPublicGenericEcho<T>(this T value, int add)
        {
            return add;
        }

        internal static int NonPublicRefRead(this ref HotReloadExtensionCallPoint value, int add)
        {
            return value.X + add;
        }

        internal static int NonPublicInRead(this in HotReloadExtensionCallPoint value, int add)
        {
            return value.X + add;
        }

        internal static int NonPublicLengthPlus(this string value, int add)
        {
            return value == null ? add + 1000 : value.Length + add;
        }

        internal static int NonPublicMarkerRead(this IHotReloadNonPublicMarker value, int add)
        {
            return add + 1;
        }

        internal static int NonPublicOrdinary(int add)
        {
            return add + 2;
        }
    }

    /// <summary>
    /// Compiled host whose lambdas and async methods call extension methods of non-public static
    /// classes. Each body's 4-digit token is unique in this file and is the value the tests edit.
    /// </summary>
    public sealed class HotReloadNonPublicClassExtensionCallHost
    {
        private int _value = 3;

        private string _label = "abc";

        private int[] _ints = { 1, 2 };

        private HotReloadExtensionCallPoint _point = new HotReloadExtensionCallPoint(5);

        private int _traceArguments;

        private HotReloadNonPublicMarked _marked = new HotReloadNonPublicMarked();

        private IHotReloadNonPublicMarker _marker = new HotReloadNonPublicMarked();

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureCallsNonPublicClassExtension()
        {
            Func<int> read = () => _value.NonPublicTriple(6101);
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public async Task<int> AsyncCallsNonPublicClassExtension()
        {
            await Task.Yield();
            return _value.NonPublicTriple(6111);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureCallsNonPublicClassExtensionInStaticForm()
        {
            Func<int> read = () => HotReloadNonPublicClassExtensions.NonPublicTriple(_value, 6121);
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureChainsNonPublicClassExtensions()
        {
            Func<int> read = () => _value.NonPublicTriple(6131).NonPublicPlusOne(1);
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureCallsPublicExtensionOfNonPublicClass()
        {
            Func<int> read = () => _value.NonPublicDouble(6141);
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureCallsNonPublicClassExtensionConditionally()
        {
            Func<int> read = () => _label?.NonPublicLengthPlus(6161) ?? 0;
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureCallsGenericNonPublicClassExtension()
        {
            Func<int> read = () => _value.NonPublicGenericEcho(6171);
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureCallsRefNonPublicClassExtension()
        {
            Func<int> read = () => _point.NonPublicRefRead(6181);
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureCallsInNonPublicClassExtension()
        {
            Func<int> read = () => _point.NonPublicInRead(6191);
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureCallsConditionalNonPublicClassExtension()
        {
            Action trace = () => _value.NonPublicTrace(RecordTraceArgument(6201));
            trace();
            return _traceArguments;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureCallsRefLikeReturningNonPublicClassExtension()
        {
            Func<int> read = () => _ints.NonPublicSpanOf().Length + 6211;
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureHoldsNonPublicClassExtensionMethodGroup()
        {
            Func<int> read = () =>
            {
                Func<int, int> lengthPlus = _label.NonPublicLengthPlus;
                return lengthPlus(6221);
            };
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureNamesTheNonPublicClassOutsideACall()
        {
            Func<int> read = () => typeof(HotReloadNonPublicClassExtensions).Name.Length + 6231;
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureCallsOrdinaryStaticMethodOfNonPublicClass()
        {
            Func<int> read = () => NonPublicOrdinary(6241);
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureCallsNonPublicClassExtensionOnNonPublicReceiverType()
        {
            Func<int> read = () => _marked.NonPublicMarkerRead(6251);
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureCallsGenericNonPublicClassExtensionInStaticForm()
        {
            Func<int> read = () => HotReloadNonPublicClassExtensions.NonPublicGenericEcho(_value, 6261);
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int StatementCallsNonPublicClassExtensionInStaticFormBesideAClosure()
        {
            int seed = HotReloadNonPublicClassExtensions.NonPublicTriple(_value, 6271);
            Func<int> read = () => _value + seed;
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureCallsNonPublicClassExtensionOnAValueOfANonPublicType()
        {
            Func<int> read = () => _marker.NonPublicMarkerRead(6281);
            return read();
        }

        private int RecordTraceArgument(int add)
        {
            _traceArguments++;
            return add;
        }
    }
}
