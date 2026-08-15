using System.Runtime.CompilerServices;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Compiled callers outside the edited host file so the signature-change gate sees
    /// uncovered call sites.
    /// </summary>
    public class HotReloadSignatureChangeExternalCaller
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public int CallTarget(int value)
        {
            return new HotReloadSignatureChangeExternalHost().Target(value);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int CallDeleted(int value)
        {
            return new HotReloadSignatureChangeExternalHost().ToDelete(value);
        }
    }
}
