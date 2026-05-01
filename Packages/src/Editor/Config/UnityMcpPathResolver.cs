using System.IO;
using UnityEngine;

namespace io.github.hatayama.uLoopMCP
{
    /// <summary>
    /// Resolves paths that are shared across CLI setup, generated skills, and editor runtime services.
    /// </summary>
    public static class UnityMcpPathResolver
    {
        public static string GetProjectRoot()
        {
            return Path.GetDirectoryName(Application.dataPath);
        }
    }
}
