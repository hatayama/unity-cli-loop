using System;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Builds compilation hints for execute-dynamic-code transpiler constraints.
    /// </summary>
    internal static class DynamicCodeTranspilerConstraintHints
    {
        internal static (bool Matched, string Hint, string Suggestion) TryBuildHint(
            string errorCode,
            string message)
        {
            if (string.Equals(errorCode, "CS8421", StringComparison.Ordinal)
                && message.Contains(DynamicCodeLiteralHoister.LiteralParameterPrefix, StringComparison.Ordinal))
            {
                return (
                    true,
                    "Static local functions cannot reference hoisted literal parameters. "
                    + "Literals inside recognized static local function bodies are kept inline automatically; "
                    + "if this error persists, the header shape may be unsupported by the scanner.",
                    "Remove the `static` modifier from the local function.");
            }

            if (string.Equals(errorCode, "CS8820", StringComparison.Ordinal)
                && message.Contains(DynamicCodeLiteralHoister.LiteralParameterPrefix, StringComparison.Ordinal))
            {
                return (
                    true,
                    "Static lambdas cannot reference hoisted literal parameters. "
                    + "Remove the `static` modifier from the lambda, or use a non-static local function instead.",
                    "Remove the `static` modifier from the lambda.");
            }

            if (string.Equals(errorCode, "CS1503", StringComparison.Ordinal)
                && message.Contains("cannot convert from 'int' to 'byte'", StringComparison.Ordinal))
            {
                return (
                    true,
                    "Literal hoisting promotes integer literals to int values. "
                    + "Unity APIs such as Color32 expect byte components and do not accept implicit int-to-byte conversion "
                    + "after hoisting, even though plain numeric literals compile in normal Unity scripts.",
                    "Cast each component explicitly, for example: new Color32((byte)255, (byte)0, (byte)0, (byte)255).");
            }

            return (false, string.Empty, string.Empty);
        }
    }
}
