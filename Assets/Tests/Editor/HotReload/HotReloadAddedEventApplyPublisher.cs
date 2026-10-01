using System;
using System.Runtime.CompilerServices;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Compiled publisher whose edited copy gains an event in an end-to-end apply, so a patched
    /// body can raise an event the compiled class does not have.
    /// </summary>
    public sealed class HotReloadAddedEventApplyPublisher
    {
        public event Action<int> Existing;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public void Raise(int value)
        {
            Existing?.Invoke(value);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void RaiseStatic(int value)
        {
        }
    }
}
