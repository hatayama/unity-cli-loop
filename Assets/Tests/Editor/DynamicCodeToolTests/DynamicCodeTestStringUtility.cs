namespace io.github.hatayama.uLoopMCP.DynamicCodeToolTests
{
    internal static class DynamicCodeTestStringUtility
    {
        public static int CountSubstring(string source, string target)
        {
            int count = 0;
            int index = 0;
            while ((index = source.IndexOf(target, index, System.StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += target.Length;
            }

            return count;
        }
    }
}
