namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    // Why: transport readiness and foreground fallback must warm identical source shapes;
    // otherwise one path can look ready while the user's first return-string execution is still cold.
    public static class ExecuteDynamicCodeReadinessProbe
    {
        public static string[] CreateReturnStringProbeCodes()
        {
            string[] source = DynamicCodeForegroundWarmupSnippets.ReturnStringShapes;
            string[] copy = new string[source.Length];
            System.Array.Copy(source, copy, source.Length);
            return copy;
        }
    }
}
