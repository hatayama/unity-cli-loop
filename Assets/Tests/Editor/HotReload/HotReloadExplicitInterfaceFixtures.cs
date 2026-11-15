using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

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

    /// <summary>An explicit implementation whose body calls a base method.</summary>
    public class HotReloadExplicitBaseCallerFixture : HotReloadExplicitBaseCallerBase, IHotReloadExplicitBaseCaller
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        int IHotReloadExplicitBaseCaller.Value(int input) { return base.Compute(input) + 8108; }
    }
}
