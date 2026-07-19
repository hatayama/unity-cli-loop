using System.IO;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Reads a single source line's trimmed text from disk, so a pause-point response can show
    /// the AI agent exactly what code the resolved (possibly rounded-forward) line number maps to.
    /// </summary>
    internal static class SourcePausePointSourceLineReader
    {
        public static string ReadLineText(string absoluteFilePath, int lineNumber)
        {
            if (string.IsNullOrEmpty(absoluteFilePath) || lineNumber <= 0 || !File.Exists(absoluteFilePath))
            {
                return string.Empty;
            }

            string[] lines = File.ReadAllLines(absoluteFilePath);
            if (lineNumber > lines.Length)
            {
                return string.Empty;
            }

            return lines[lineNumber - 1].Trim();
        }
    }
}
