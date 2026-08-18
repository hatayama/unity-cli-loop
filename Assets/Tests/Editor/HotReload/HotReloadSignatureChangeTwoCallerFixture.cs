using System.Runtime.CompilerServices;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Compiled host with two same-file callers of one target, so a mixed
    /// never-patched / already-patched caller run can share one fixture.
    /// </summary>
    public class HotReloadSignatureChangeTwoCallerFixture
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public int Target(int value)
        {
            return value;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public long CallerAlpha(int value)
        {
            return Target(value);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public long CallerBeta(int value)
        {
            return Target(value);
        }
    }
}
