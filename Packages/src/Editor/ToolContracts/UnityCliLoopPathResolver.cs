using System.IO;
using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.ToolContracts
{
    /// <summary>
    /// Resolves paths that are shared across CLI setup, generated skills, and editor runtime services.
    /// </summary>
    public static class UnityCliLoopPathResolver
    {
        public static string GetProjectRoot()
        {
            return Path.GetDirectoryName(UnityEngine.Application.dataPath);
        }
    }
}
