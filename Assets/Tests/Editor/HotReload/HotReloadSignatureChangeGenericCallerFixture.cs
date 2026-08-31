using System.Runtime.CompilerServices;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Compiled host where a generic Caller&lt;T&gt;(int) invokes Target and a non-generic
    /// Caller(int) shares the name and parameter list. Used to prove the gate does not
    /// treat an edited non-generic Caller as covering the generic compiled caller.
    /// </summary>
    public class HotReloadSignatureChangeGenericCallerFixture
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public int Target(int value)
        {
            return value;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int Caller(int value)
        {
            return value;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int Caller<T>(int value)
        {
            return Target(value);
        }
    }
}
