using System.Runtime.CompilerServices;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Compiled API whose signature names the compiled enum, so an added method calling it
    /// while a reload builds that enum from source cannot bind.
    /// </summary>
    // Why public: added methods compile in a separate shim assembly.
    public static class HotReloadCarriedInNextStepRegistry
    {
        // Why NoInlining: a test patches this body, and an inlined copy at a call site would keep
        // running the compiled body.
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Accept(HotReloadSiblingEnum kind)
        {
            return (int)kind;
        }
    }
}
