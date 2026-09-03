namespace io.github.hatayama.UnityCliLoop.ToolContracts
{
    /// <summary>
    /// Builds the Message suffix that points a caller at a response's Warnings array.
    /// </summary>
    public static class WarningsMessagePointer
    {
        // One definition for every tool that reports warnings: an agent learns "read Message, follow
        // the pointer" once, and a second wording of the same suffix would make that habit
        // tool-specific. The Go CLI matches this exact text when it restates the count.
        private const string CountSuffixText = " warning(s). See Warnings.";

        /// <summary>
        /// Appends the warning count and the pointer to Warnings, leaving the message untouched
        /// when nothing warned.
        /// </summary>
        public static string Append(string message, int warningCount)
        {
            if (warningCount <= 0)
            {
                return message;
            }

            string suffix = warningCount + CountSuffixText;
            return string.IsNullOrEmpty(message) ? suffix : message + " " + suffix;
        }
    }
}
