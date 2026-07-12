using System;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Builds compilation hints for execute-dynamic-code transpiler constraints.
    /// </summary>
    internal static class DynamicCodeTranspilerConstraintHints
    {
        internal static bool TryBuildHint(
            string errorCode,
            string message,
            out string hint,
            out string suggestion)
        {
            hint = string.Empty;
            suggestion = string.Empty;

            if (string.Equals(errorCode, "CS8421", StringComparison.Ordinal)
                && message.Contains(LiteralParameterPrefix, StringComparison.Ordinal))
            {
                hint =
                    "Static local functions cannot reference hoisted literal parameters. "
                    + "Keep literals used inside static local functions as inline constants, "
                    + "or move the helper outside the static local function.";
                suggestion = "Replace hoisted literals inside the static local function body with inline constants.";
                return true;
            }

            if (string.Equals(errorCode, "CS1503", StringComparison.Ordinal)
                && message.Contains("cannot convert from 'int' to 'byte'", StringComparison.Ordinal))
            {
                hint =
                    "Literal hoisting promotes integer literals to int values. "
                    + "Unity APIs such as Color32 expect byte components and do not accept implicit int-to-byte conversion "
                    + "after hoisting, even though plain numeric literals compile in normal Unity scripts.";
                suggestion = "Cast each component explicitly, for example: new Color32((byte)255, (byte)0, (byte)0, (byte)255).";
                return true;
            }

            return false;
        }

        private const string LiteralParameterPrefix = "__uloop_literal_";
    }
}
