using System.IO;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Reads the trimmed source text for a resolved pause-point line so enable and hot-reload
    /// retarget paths share one helper.
    /// </summary>
    internal static class PausePointLineTextReader
    {
        public static string ReadResolvedLineText(string requestedFile, int resolvedLine)
        {
            if (string.IsNullOrEmpty(requestedFile) || resolvedLine <= 0)
            {
                return string.Empty;
            }

            string normalizedFile = SourcePausePointPathNormalizer.ToForwardSlashes(requestedFile);
            string absoluteFilePath = Path.Combine(UnityCliLoopPathResolver.GetProjectRoot(), normalizedFile);
            return SourcePausePointSourceLineReader.ReadLineText(absoluteFilePath, resolvedLine);
        }
    }
}
