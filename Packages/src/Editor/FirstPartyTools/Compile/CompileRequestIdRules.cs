namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Defines the request ID character contract shared by compile wait and result persistence.
    /// </summary>
    internal static class CompileRequestIdRules
    {
        internal static bool IsSafe(string requestId)
        {
            foreach (char character in requestId)
            {
                bool isSafe = (character >= 'a' && character <= 'z') ||
                              (character >= 'A' && character <= 'Z') ||
                              (character >= '0' && character <= '9') ||
                              character == '_' ||
                              character == '-';
                if (!isSafe)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
