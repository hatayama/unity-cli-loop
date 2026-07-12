using System.Diagnostics;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Maps wrapped-source compiler columns to user-snippet columns.
    /// </summary>
    internal static class DynamicCodeDiagnosticColumnMapper
    {
        internal static int MapWrappedColumnToUserColumn(int wrappedColumn1Based)
        {
            Debug.Assert(wrappedColumn1Based >= 0, "wrappedColumn1Based must not be negative");

            if (wrappedColumn1Based <= 0)
            {
                return wrappedColumn1Based;
            }

            int userColumn = wrappedColumn1Based - WrapperTemplate.UserBodyIndentSpaces;
            if (userColumn < 1)
            {
                return 1;
            }

            return userColumn;
        }
    }
}
