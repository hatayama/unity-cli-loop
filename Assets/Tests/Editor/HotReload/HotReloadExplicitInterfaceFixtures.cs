using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using HotReloadExplicitRetargetedArgument = io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload.HotReloadExplicitArgA;
using HotReloadExplicitRetargetedInterface = io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload.IHotReloadExplicitA;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>Interface whose single method the fixtures implement explicitly.</summary>
    public interface IHotReloadExplicitRunner
    {
        int Run();
    }

    /// <summary>Generic interface the box fixture implements over a closed type argument.</summary>
    public interface IHotReloadExplicitBox<T>
    {
        T Take(T value);
    }

    /// <summary>Declares a nested interface, so the implementing method's name holds a nested type.</summary>
    public class HotReloadExplicitOuter
    {
        /// <summary>Interface nested in a type.</summary>
        public interface IInner
        {
            int Run();
        }
    }

    /// <summary>Interface with a read-write property.</summary>
    public interface IHotReloadExplicitValue
    {
        int Value { get; set; }
    }

    /// <summary>Interface with a read-only property, implemented with an expression body.</summary>
    public interface IHotReloadExplicitArrowValue
    {
        int Value { get; }
    }

    /// <summary>Interface with a read-only property, implemented with a get accessor.</summary>
    public interface IHotReloadExplicitReadOnlyValue
    {
        int Reading { get; }
    }

    /// <summary>Interface with an init-only property.</summary>
    public interface IHotReloadExplicitInitValue
    {
        int Value { get; init; }
    }

    /// <summary>Interface with a write-only property.</summary>
    public interface IHotReloadExplicitSetOnlyValue
    {
        int Written { set; }
    }

    /// <summary>Interface with an indexer.</summary>
    public interface IHotReloadExplicitIndexed
    {
        int this[int index] { get; }
    }

    /// <summary>Interface with an event.</summary>
    public interface IHotReloadExplicitNotifier
    {
        event Action Changed;
    }

    /// <summary>First argument type of the overloaded interface, which a using alias can name.</summary>
    public sealed class HotReloadExplicitArgA
    {
    }

    /// <summary>Second argument type of the overloaded interface, which a using alias can name.</summary>
    public sealed class HotReloadExplicitArgB
    {
    }

    /// <summary>Interface with two overloads that differ only in the argument type.</summary>
    public interface IHotReloadExplicitOverloaded
    {
        int Take(HotReloadExplicitArgA value);

        int Take(HotReloadExplicitArgB value);
    }

    /// <summary>Generic interface with a read-write property.</summary>
    public interface IHotReloadExplicitBoxValue<T>
    {
        T Value { get; set; }
    }

    /// <summary>Declares a nested interface with a property.</summary>
    public class HotReloadExplicitValueOuter
    {
        /// <summary>Interface nested in a type, with a property.</summary>
        public interface IInnerValue
        {
            int Value { get; }
        }
    }

    /// <summary>First of two interfaces whose members share a name.</summary>
    public interface IHotReloadExplicitA
    {
        int Run();
    }

    /// <summary>Second of two interfaces whose members share a name.</summary>
    public interface IHotReloadExplicitB
    {
        int Run();
    }

    /// <summary>Interface with an async member.</summary>
    public interface IHotReloadExplicitAsync
    {
        Task<int> RunAsync();
    }

    /// <summary>Interface with a synchronous member whose explicit body uses a closure.</summary>
    public interface IHotReloadExplicitClosure
    {
        int Read();
    }

    /// <summary>Interface with a generic method.</summary>
    public interface IHotReloadExplicitFactory
    {
        T Make<T>();
    }

    /// <summary>Interface implemented by the fixture whose explicit body calls a base method.</summary>
    public interface IHotReloadExplicitBaseCaller
    {
        int Value(int input);
    }

    /// <summary>Base type whose method an explicit implementation calls through base.</summary>
    public class HotReloadExplicitBaseCallerBase
    {
        public virtual int Compute(int input) { return input + 100; }
    }

    // Why NoInlining on every explicit body: without it the JIT can inline an original body into a
    // test compiled before the apply, so the test would still see the old value after the patch.
    // Each "81xx" number is the unique token a test replaces to edit exactly that body.

    /// <summary>An explicit implementation of an interface declared in a namespace.</summary>
    public class HotReloadExplicitRunnerFixture : IHotReloadExplicitRunner
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        int IHotReloadExplicitRunner.Run() { return 8101; }
    }

    /// <summary>An explicit implementation of a closed generic interface.</summary>
    public class HotReloadExplicitBoxFixture : IHotReloadExplicitBox<int>
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        int IHotReloadExplicitBox<int>.Take(int value) { return value + 8102; }
    }

    /// <summary>An explicit implementation of an interface nested in a type.</summary>
    public class HotReloadExplicitNestedFixture : HotReloadExplicitOuter.IInner
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        int HotReloadExplicitOuter.IInner.Run() { return 8103; }
    }

    /// <summary>Two explicit implementations and an ordinary method, all named Run.</summary>
    public class HotReloadExplicitPairFixture : IHotReloadExplicitA, IHotReloadExplicitB
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        int IHotReloadExplicitA.Run() { return 8104; }

        [MethodImpl(MethodImplOptions.NoInlining)]
        int IHotReloadExplicitB.Run() { return 8105; }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int Run() { return 8106; }
    }

    /// <summary>
    /// An async explicit implementation whose lambda reads a private field, which the patch can
    /// only reach through an accessor.
    /// </summary>
    public class HotReloadExplicitAsyncFixture : IHotReloadExplicitAsync
    {
        private int _seed = 5;

        [MethodImpl(MethodImplOptions.NoInlining)]
        async Task<int> IHotReloadExplicitAsync.RunAsync()
        {
            await Task.FromResult(0);
            Func<int> read = () => _seed;
            return read() + 8107;
        }
    }

    /// <summary>
    /// A synchronous explicit implementation whose lambda reads a private field, which the patch
    /// reaches through closure accessors.
    /// </summary>
    public class HotReloadExplicitClosureFixture : IHotReloadExplicitClosure
    {
        private int _seed = 7;

        [MethodImpl(MethodImplOptions.NoInlining)]
        int IHotReloadExplicitClosure.Read()
        {
            Func<int> read = () => _seed;
            return read() + 8109;
        }
    }

    /// <summary>An explicit implementation on a generic class, which the generic gate keeps Skipped.</summary>
    public class HotReloadExplicitGenericHostFixture<T> : IHotReloadExplicitRunner
    {
        int IHotReloadExplicitRunner.Run() { return 8110; }
    }

    /// <summary>A generic explicit method, which the generic gate keeps Skipped.</summary>
    public class HotReloadExplicitGenericMethodFixture : IHotReloadExplicitFactory
    {
        T IHotReloadExplicitFactory.Make<T>() { int marker = 8111; return marker > 0 ? default(T) : default(T); }
    }

    /// <summary>An explicit read-write property with a block-bodied getter and setter.</summary>
    public class HotReloadExplicitValueFixture : IHotReloadExplicitValue
    {
        private int _stored = 1;

        int IHotReloadExplicitValue.Value
        {
            [MethodImpl(MethodImplOptions.NoInlining)]
            get { return _stored + 8201; }
            [MethodImpl(MethodImplOptions.NoInlining)]
            set { _stored = value + 8202; }
        }
    }

    /// <summary>An explicit read-only property with an expression body.</summary>
    public class HotReloadExplicitArrowValueFixture : IHotReloadExplicitArrowValue
    {
        int IHotReloadExplicitArrowValue.Value => 8203;
    }

    /// <summary>An explicit read-only property, which an edit may give a setter.</summary>
    public class HotReloadExplicitReadOnlyValueFixture : IHotReloadExplicitReadOnlyValue
    {
        int IHotReloadExplicitReadOnlyValue.Reading
        {
            [MethodImpl(MethodImplOptions.NoInlining)]
            get { return 8204; }
        }
    }

    /// <summary>An explicit write-only property, which an edit may give a getter.</summary>
    public class HotReloadExplicitSetOnlyValueFixture : IHotReloadExplicitSetOnlyValue
    {
        private int _stored;

        int IHotReloadExplicitSetOnlyValue.Written
        {
            [MethodImpl(MethodImplOptions.NoInlining)]
            set { _stored = value + 8208; }
        }
    }

    /// <summary>An ordinary write-only property, which an edit may give a getter.</summary>
    public class HotReloadOrdinarySetOnlyValueFixture
    {
        private int _stored;

        public int OrdinaryWritten
        {
            set { _stored = value + 8209; }
        }
    }

    /// <summary>An explicit indexer, whose accessors stay Skipped.</summary>
    public class HotReloadExplicitIndexedFixture : IHotReloadExplicitIndexed
    {
        int IHotReloadExplicitIndexed.this[int index]
        {
            [MethodImpl(MethodImplOptions.NoInlining)]
            get { return index + 8210; }
        }
    }

    /// <summary>
    /// An explicit implementation whose interface is named through a using alias, beside a public
    /// method that implements the other interface, so retargeting the alias moves it there.
    /// </summary>
    public class HotReloadExplicitRetargetedFixture : IHotReloadExplicitA, IHotReloadExplicitB
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        int HotReloadExplicitRetargetedInterface.Run() { return 8141; }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int Run() { return 8142; }
    }

    /// <summary>
    /// An explicit implementation whose argument type is named through a using alias, beside public
    /// methods for both overloads, so retargeting the alias moves it to the other overload while the
    /// class still implements both.
    /// </summary>
    public class HotReloadExplicitRetargetedOverloadFixture : IHotReloadExplicitOverloaded
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        int IHotReloadExplicitOverloaded.Take(HotReloadExplicitRetargetedArgument value) { return 8143; }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int Take(HotReloadExplicitArgA value) { return 8144; }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int Take(HotReloadExplicitArgB value) { return 8145; }
    }

    /// <summary>An explicit event with add and remove accessors.</summary>
    public class HotReloadExplicitNotifierFixture : IHotReloadExplicitNotifier
    {
        private Action _handlers;

        event Action IHotReloadExplicitNotifier.Changed
        {
            add { _handlers += value; }
            remove { _handlers -= value; }
        }
    }

    /// <summary>An explicit property with an init accessor, which stays Skipped.</summary>
    public class HotReloadExplicitInitValueFixture : IHotReloadExplicitInitValue
    {
        private int _stored;

        int IHotReloadExplicitInitValue.Value
        {
            get { return _stored; }
            init { _stored = value + 8205; }
        }
    }

    /// <summary>An explicit property of a closed generic interface.</summary>
    public class HotReloadExplicitBoxValueFixture : IHotReloadExplicitBoxValue<int>
    {
        private int _stored = 1;

        int IHotReloadExplicitBoxValue<int>.Value
        {
            [MethodImpl(MethodImplOptions.NoInlining)]
            get { return _stored + 8206; }
            [MethodImpl(MethodImplOptions.NoInlining)]
            set { _stored = value; }
        }
    }

    /// <summary>An explicit property of an interface nested in a type.</summary>
    public class HotReloadExplicitNestedValueFixture : HotReloadExplicitValueOuter.IInnerValue
    {
        int HotReloadExplicitValueOuter.IInnerValue.Value
        {
            [MethodImpl(MethodImplOptions.NoInlining)]
            get { return 8207; }
        }
    }

    /// <summary>An explicit implementation whose body calls a base method.</summary>
    public class HotReloadExplicitBaseCallerFixture : HotReloadExplicitBaseCallerBase, IHotReloadExplicitBaseCaller
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        int IHotReloadExplicitBaseCaller.Value(int input) { return base.Compute(input) + 8108; }
    }
}
