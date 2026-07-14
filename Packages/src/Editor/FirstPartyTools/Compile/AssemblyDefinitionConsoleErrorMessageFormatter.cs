using System.Diagnostics;
using System.Linq;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Formats Assembly Definition and Assembly Reference Console errors into a compile failure message.
    /// </summary>
    internal static class AssemblyDefinitionConsoleErrorMessageFormatter
    {
        private const int MaxDisplayedIssueCount = 10;

        /// <summary>
        /// Creates the compile failure message shown when Assembly Definition or Assembly Reference errors are present.
        /// </summary>
        internal static string CreateFailureMessage(AssemblyDefinitionConsoleError[] errors)
        {
            Debug.Assert(errors != null, "errors must not be null");

            string details = string.Join(
                "\n",
                errors
                    .Take(MaxDisplayedIssueCount)
                    .Select(error => string.IsNullOrWhiteSpace(error.File)
                        ? $"- {error.Message}"
                        : $"- {error.File}: {error.Message}")
            );

            return $"{UnityCliLoopConstants.ERROR_MESSAGE_ASSEMBLY_DEFINITION_IMPORT_ERROR}\n{details}";
        }
    }
}
