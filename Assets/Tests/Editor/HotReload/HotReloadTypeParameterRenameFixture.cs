using System.Runtime.CompilerServices;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Compiled generic method, its caller, and a caller of a generic type's method, for tests that
    /// rename a type parameter in the edited source while keeping its arity and positions.
    /// </summary>
    public sealed class HotReloadTypeParameterRenameFixture
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public int Echo<T>(T value)
        {
            return 10;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int CallEcho()
        {
            return Echo<int>(1) + 100;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int CallRead()
        {
            return new HotReloadTypeParameterRenameGenericHost<int>().Read(1) + 300;
        }
    }

    /// <summary>
    /// Compiled generic type for tests that rename the type's type parameter in the edited source.
    /// </summary>
    public sealed class HotReloadTypeParameterRenameGenericHost<T>
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public int Read(T value)
        {
            return 30;
        }
    }
}
