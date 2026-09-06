using System.IO;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Writes an edited copy of a fixture source under the hot-reload test-sources directory.
    /// </summary>
    /// <remarks>
    /// Why outside Assets/: an edited copy inside the project would trigger AssetDatabase import
    /// and a real compile in the middle of a test. Shared so the orchestrator tests and the
    /// cross-file end-to-end tests write their copies to the same place.
    /// </remarks>
    internal static class HotReloadTestSourceWriter
    {
        internal static string WriteEditedSource(string fileName, string contents)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string directory = Path.Combine(projectRoot, HotReloadConstants.TestSourcesRelativeDirectory);
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, fileName);
            File.WriteAllText(path, contents);
            return path;
        }
    }
}
