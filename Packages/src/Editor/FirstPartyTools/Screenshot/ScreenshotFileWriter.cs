using System;
using System.IO;
using System.Text;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Resolves screenshot output paths and writes captured textures to disk.
    /// </summary>
    internal static class ScreenshotFileWriter
    {
        private const string InvalidFileNameCharacters = "<>:\"/\\|?*";

        internal static string EnsureOutputDirectoryExists(string outputDirectory)
        {
            string resolvedDirectory;

            if (string.IsNullOrEmpty(outputDirectory))
            {
                string projectRoot = UnityCliLoopPathResolver.GetProjectRoot();
                resolvedDirectory = Path.Combine(projectRoot, UnityCliLoopConstants.OUTPUT_ROOT_DIR, UnityCliLoopConstants.SCREENSHOTS_DIR);
            }
            else
            {
                resolvedDirectory = Path.GetFullPath(outputDirectory);
            }

            Directory.CreateDirectory(resolvedDirectory);

            return resolvedDirectory;
        }

        internal static string SanitizeFileName(string name)
        {
            StringBuilder sanitized = new StringBuilder(name.Length);
            foreach (char character in name)
            {
                bool isInvalid = character < ' '
                    || InvalidFileNameCharacters.IndexOf(character) >= 0;
                sanitized.Append(isInvalid ? '_' : character);
            }

            return sanitized.ToString();
        }

        internal static void SaveTextureAsPng(Texture2D texture, string fullPath)
        {
            byte[] pngData = texture.EncodeToPNG();
            if (pngData == null)
            {
                throw new InvalidOperationException($"Failed to encode texture to PNG. Format: {texture.format}, Size: {texture.width}x{texture.height}");
            }
            File.WriteAllBytes(fullPath, pngData);
        }
    }
}
