using System;
using System.IO;

using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Deletes cached rewritten copies of one assembly image, so the next request for a copy
    /// rewrites the image under the current rule. A copy is keyed by the image's name and Mvid only,
    /// so a copy written before a rule change would otherwise be returned without the rule running.
    /// </summary>
    internal static class PublicizedCopyTestCache
    {
        /// <summary>
        /// Deletes the "&lt;assemblyName&gt;-&lt;Mvid&gt;.dll" copies under the project-relative
        /// <paramref name="relativeDirectory"/>. Copies of an assembly whose name only starts with
        /// <paramref name="assemblyName"/> stay.
        /// </summary>
        internal static void DeleteCopiesOf(string assemblyName, string relativeDirectory)
        {
            string projectRootPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string outputDirectory = Path.Combine(projectRootPath, relativeDirectory);
            if (!Directory.Exists(outputDirectory))
            {
                return;
            }

            foreach (string candidatePath in Directory.GetFiles(outputDirectory, assemblyName + "-*.dll"))
            {
                string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(candidatePath);
                if (fileNameWithoutExtension.Length <= assemblyName.Length + 1)
                {
                    continue;
                }

                string mvidCandidate = fileNameWithoutExtension.Substring(assemblyName.Length + 1);
                if (Guid.TryParseExact(mvidCandidate, "N", out Guid _))
                {
                    File.Delete(candidatePath);
                }
            }
        }
    }
}
