using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Keeps hidden warmup aligned with the literal-hoisted return-string shapes users commonly execute first.
    /// </summary>
    internal static class DynamicCodeForegroundWarmupSnippets
    {
        internal static readonly string[] ReturnStringShapes =
        {
            "return \"" + UnityCliLoopConstants.DISPLAY_NAME + " dynamic code prewarm\";",
            "return\n  \"" + UnityCliLoopConstants.DISPLAY_NAME + " dynamic code prewarm\";"
        };
    }
}
