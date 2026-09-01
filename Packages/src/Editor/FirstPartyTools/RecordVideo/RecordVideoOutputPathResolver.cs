using System;
using System.IO;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Resolves the record-video output path without creating directories.
    /// </summary>
    internal static class RecordVideoOutputPathResolver
    {
        private const string Mp4Extension = "mp4";
        private const string WebmExtension = "webm";
        private const string DefaultFileNamePrefix = "gameview_";
        private const string TimestampFormat = "yyyyMMdd_HHmmss";

        internal static string Resolve(
            string requestedPath,
            string projectRoot,
            DateTime now,
            bool isLinux)
        {
            if (string.IsNullOrEmpty(requestedPath))
            {
                string extension = isLinux ? WebmExtension : Mp4Extension;
                string fileName = $"{DefaultFileNamePrefix}{now.ToString(TimestampFormat)}.{extension}";
                return Path.Combine(
                    projectRoot,
                    UnityCliLoopConstants.OUTPUT_ROOT_DIR,
                    UnityCliLoopConstants.VIDEOS_DIR,
                    fileName);
            }

            return Path.GetFullPath(requestedPath);
        }
    }
}
