using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Struct receiver for the extension methods below, read by value or by reference.
    /// </summary>
    public struct HotReloadExtensionCallPoint
    {
        public int X;

        public HotReloadExtensionCallPoint(int x)
        {
            X = x;
        }
    }

    /// <summary>
    /// Public class whose extension methods are internal, declared in the same file as the host,
    /// so a lambda or async body that calls one needs an accessor.
    /// </summary>
    public static class HotReloadPublicClassInternalExtensions
    {
        internal static int TriplePlus(this int value, int add)
        {
            return value * 3 + add;
        }

        internal static int PlusOne(this int value, int add)
        {
            return value + add + 1;
        }

        internal static int NullableLengthPlus(this string value, int add)
        {
            return value == null ? add + 1000 : value.Length + add;
        }

        internal static int SumAll(this int value, params int[] values)
        {
            int sum = value;
            foreach (int item in values)
            {
                sum += item;
            }

            return sum;
        }

        internal static int SumValues(params int[] values)
        {
            int sum = 0;
            foreach (int item in values)
            {
                sum += item;
            }

            return sum;
        }

        // The condition symbol is never defined, so the compiler omits every call to these two.
        [Conditional("ULOOP_HOT_RELOAD_TEST_UNDEFINED_SYMBOL")]
        internal static void TraceCall(this int value, int add)
        {
            throw new InvalidOperationException("A [Conditional] call must be omitted.");
        }

        [Conditional("ULOOP_HOT_RELOAD_TEST_UNDEFINED_SYMBOL")]
        internal static void TraceOrdinary(int add)
        {
            throw new InvalidOperationException("A [Conditional] call must be omitted.");
        }

        internal static ReadOnlySpan<int> SpanOf(int[] values)
        {
            return new ReadOnlySpan<int>(values);
        }

        internal static int GenericEcho<T>(this T value, int add)
        {
            return add;
        }

        internal static int RefPointRead(this ref HotReloadExtensionCallPoint value, int add)
        {
            return value.X + add;
        }

        internal static int InPointRead(this in HotReloadExtensionCallPoint value, int add)
        {
            return value.X + add;
        }

        internal static int PointRead(this HotReloadExtensionCallPoint value, int add)
        {
            return value.X + add;
        }
    }

    /// <summary>
    /// Public class with an internal instance method, reached through a public member of
    /// <see cref="HotReloadExtensionCallBox"/>.
    /// </summary>
    public sealed class HotReloadExtensionCallInner
    {
        internal int Plus(int add)
        {
            return add + 1;
        }
    }

    /// <summary>
    /// Holds a <see cref="HotReloadExtensionCallInner"/> behind a public property, so a call on
    /// it can sit further down a conditional-access chain.
    /// </summary>
    public sealed class HotReloadExtensionCallBox
    {
        public HotReloadExtensionCallInner Inner { get; } = new HotReloadExtensionCallInner();
    }

    /// <summary>
    /// Compiled host whose lambdas and async methods call internal extension methods of a public
    /// class. Each body's 4-digit token is unique in this file and is the value the tests edit.
    /// </summary>
    public sealed class HotReloadPublicClassExtensionCallHost
    {
        private int _value = 3;

        private string _label = "abc";

        private string _missingLabel = null;

        private HotReloadExtensionCallPoint _point = new HotReloadExtensionCallPoint(5);

        private string _order = "";

        private string[] _labels = { "abcd" };

        private HotReloadExtensionCallBox _box = new HotReloadExtensionCallBox();

        private int _stored;

        private int[] _ints = { 1, 2 };

        private int _traceArguments;

        private int _receiverCalls;

        public string Order => _order;

        public int ReceiverCalls => _receiverCalls;

        public int ClosureGetterValue
        {
            get
            {
                Func<int> read = () => _value.TriplePlus(7301);
                return read();
            }
        }

        public int ClosureSetterValue
        {
            get => _stored;
            set
            {
                Func<int> read = () => value.TriplePlus(7311);
                _stored = read();
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureCallsInternalExtension()
        {
            Func<int> read = () => _value.TriplePlus(7101);
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public async Task<int> AsyncCallsInternalExtension()
        {
            await Task.Yield();
            return _value.TriplePlus(7111);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureCallsInternalExtensionInStaticForm()
        {
            Func<int> read = () => HotReloadPublicClassInternalExtensions.TriplePlus(_value, 7121);
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureChainsInternalExtensions()
        {
            Func<int> read = () => _value.TriplePlus(7131).PlusOne(1);
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureCallsInternalExtensionOnCallResult()
        {
            Func<int> read = () => NextReceiver().TriplePlus(RecordArgument(7141));
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureCallsInternalExtensionConditionally()
        {
            Func<int> read = () => _label?.NullableLengthPlus(7151) ?? 0;
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureCallsInternalExtensionAfterConditionalAccess()
        {
            Func<int> read = () => _label?.Length.TriplePlus(7221) ?? 0;
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureCallsInternalExtensionAfterConditionalCall()
        {
            Func<int> read = () => _label?.ToUpperInvariant().NullableLengthPlus(7251) ?? 0;
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureCallsInternalExtensionAfterConditionalElement()
        {
            Func<int> read = () => _labels?[0].NullableLengthPlus(7261) ?? 0;
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureCallsInternalMethodAfterConditionalAccess()
        {
            Func<int> read = () => _box?.Inner.Plus(7271) ?? 0;
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureCallsParamsInternalExtensionWithNoElements()
        {
            Func<int> read = () => _value.SumAll() + 7281;
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureCallsParamsInternalMethodExpanded()
        {
            Func<int> read = () => HotReloadPublicClassInternalExtensions.SumValues(7291);
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureCallsConditionalInternalExtension()
        {
            Action trace = () => _value.TraceCall(RecordTraceArgument(7321));
            trace();
            return _traceArguments;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureCallsConditionalInternalMethod()
        {
            Action trace = () => HotReloadPublicClassInternalExtensions.TraceOrdinary(RecordTraceArgument(7331));
            trace();
            return _traceArguments;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int StatementCallsConditionalInternalMethod()
        {
            HotReloadPublicClassInternalExtensions.TraceOrdinary(RecordTraceArgument(1));
            return _traceArguments + 7371;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureCallsRefLikeReturningInternalMethod()
        {
            Func<int> read = () => HotReloadPublicClassInternalExtensions.SpanOf(_ints).Length + 7361;
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureCallsParamsInternalExtensionExpanded()
        {
            Func<int> read = () => _value.SumAll(7231);
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureCallsParamsInternalExtensionWithArray()
        {
            Func<int> read = () => _value.SumAll(new[] { 7241 });
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureCallsGenericInternalExtension()
        {
            Func<int> read = () => _value.GenericEcho(7161);
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureCallsRefInternalExtension()
        {
            Func<int> read = () => _point.RefPointRead(7171);
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureCallsInInternalExtension()
        {
            Func<int> read = () => _point.InPointRead(7181);
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureCallsInternalExtensionOnStructByValue()
        {
            Func<int> read = () => _point.PointRead(7191);
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureCallsInternalExtensionOnNullReceiver()
        {
            Func<int> read = () => _missingLabel.NullableLengthPlus(7201);
            return read();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ClosureHoldsInternalExtensionMethodGroup()
        {
            Func<int> read = () =>
            {
                Func<int, int> lengthPlus = _label.NullableLengthPlus;
                return lengthPlus(7211);
            };
            return read();
        }

        private int NextReceiver()
        {
            _order += "r";
            _receiverCalls++;
            return _value;
        }

        private int RecordTraceArgument(int add)
        {
            _traceArguments++;
            return add;
        }

        private int RecordArgument(int add)
        {
            _order += "a";
            return add;
        }
    }
}
