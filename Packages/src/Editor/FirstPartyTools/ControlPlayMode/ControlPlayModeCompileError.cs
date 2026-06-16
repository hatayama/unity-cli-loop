using UnityEditor.Compilation;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Carries a compiler error that blocked PlayMode entry.
    /// </summary>
    public sealed class ControlPlayModeCompileError
    {
        public string Message { get; set; }
        public string File { get; set; }
        public int Line { get; set; }

        internal static ControlPlayModeCompileError FromCompilerMessage(CompilerMessage compilerMessage)
        {
            UnityEngine.Debug.Assert(compilerMessage.type == CompilerMessageType.Error, "compilerMessage must be an error");

            return new ControlPlayModeCompileError
            {
                Message = compilerMessage.message ?? string.Empty,
                File = compilerMessage.file ?? string.Empty,
                Line = compilerMessage.line
            };
        }
    }
}
