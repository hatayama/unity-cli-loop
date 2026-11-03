namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Keeps hidden warmup aligned with the literal-hoisted return-string shapes users commonly execute first.
    /// </summary>
    internal static class DynamicCodeForegroundWarmupSnippets
    {
        internal static readonly string[] ReturnStringShapes =
        {
            "return \"Unity Loop Harness dynamic code prewarm\";",
            "return\n  \"Unity Loop Harness dynamic code prewarm\";"
        };
    }
}
