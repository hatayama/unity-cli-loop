using System.Runtime.CompilerServices;

using T = System;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Generic method whose type parameter shares its name with a using alias that a parameter
    /// type names through '::', for worker tests that rename the type parameter only.
    /// </summary>
    public sealed class HotReloadTypeParameterRenameAliasFixture
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public int Format<T>(T::String text, T value)
        {
            return 2400;
        }
    }
}
