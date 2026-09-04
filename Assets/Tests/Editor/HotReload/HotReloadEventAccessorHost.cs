using System.Runtime.CompilerServices;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    public delegate void HotReloadScoreDelegate(int amount);

    // Not visible outside the assembly, so an event of this type cannot be named by the shim.
    internal delegate void HotReloadHiddenScoreDelegate(int amount);

    /// <summary>
    /// Compiled host for event-accessor worker tests: a field-like instance event, a static one,
    /// and one whose delegate type is not externally visible. Tests copy this source, replace the
    /// method bodies with event raises, and run the transform worker against the compiled
    /// assembly as ground truth.
    /// </summary>
    public class HotReloadEventAccessorHost
    {
        public event HotReloadScoreDelegate Scored;

        public static event HotReloadScoreDelegate StaticScored;

        internal event HotReloadHiddenScoreDelegate HiddenScored;

        // Custom accessors mean no compiler-generated backing field, so a test can edit this
        // declaration into a field-like event and check that the raiser is still skipped.
        public event HotReloadScoreDelegate CustomScored
        {
            add { _customScored = _customScored + value; }
            remove { _customScored = _customScored - value; }
        }

        private HotReloadScoreDelegate _customScored;

        public int Total;

        // Lets a test raise an event through a conditional receiver ('Other?.Scored').
        public HotReloadEventAccessorHost Other;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public void RaiseScored(int amount)
        {
            Total = amount;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public void RaiseStaticScored(int amount)
        {
            Total = amount;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public void ClearScored()
        {
            Total = 0;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public void RaiseHiddenScored(int amount)
        {
            Total = amount;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public void RaiseCustomScored(int amount)
        {
            Total = amount;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public void RaiseOtherScored(int amount)
        {
            Total = amount;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public void OnScored(int amount)
        {
            Total = Total + amount;
        }
    }
}
