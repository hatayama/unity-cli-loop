namespace io.github.hatayama.uLoopMCP.DynamicCodeToolTests
{
    internal static class DynamicCodeTestStringUtility
    {
        public static int CountSubstring(string source, string target)
        {
            if (string.IsNullOrEmpty(target))
            {
                return 0;
            }

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
