#if UNITY_EDITOR
using NUnit.Framework;

namespace UnityCliLoop.RunTestsPredefinedAssemblyProbe
{
    /// <summary>
    /// NUnit marker compiled into a predefined assembly for the TypeCache smoke test.
    /// </summary>
    public sealed class RunTestsPredefinedAssemblyProbe
    {
        // Why this file lives under Assets/Util with no .asmdef: deliberately
        // outside any asmdef so it compiles into predefined Assembly-CSharp;
        // with this project's default settings (playModeTestRunnerEnabled=0)
        // neither EditMode nor PlayMode discovery includes it, and only the
        // TypeCache smoke test references it. #if UNITY_EDITOR keeps NUnit
        // out of player builds.
        [Test]
        public void Marker()
        {
        }
    }
}
#endif
