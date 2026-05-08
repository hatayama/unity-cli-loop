namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    // Shares the return-string warmup shape with compile and server-recovery tool-path warmups.
    public static class ExecuteDynamicCodeWarmup
    {
        public static string[] CreateReturnStringWarmupCodes()
        {
            string[] source = DynamicCodeForegroundWarmupSnippets.ReturnStringShapes;
            string[] copy = new string[source.Length];
            System.Array.Copy(source, copy, source.Length);
            return copy;
        }
    }
}
