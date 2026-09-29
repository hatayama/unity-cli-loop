using System;
using System.IO;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Deletes the introduced-type artifacts that earlier domains left under the project's Library,
    /// together with the reference-cache copies made from them.
    /// </summary>
    internal sealed class HotReloadIntroducedTypeArtifactSweeper
    {
        private const string GuidFormat = "N";
        private const int ArtifactIdLength = 32;
        private const char ArtifactCopyNameSeparator = '-';

        private readonly string _projectRoot;
        private readonly string _currentSessionId;
        private readonly Guid _currentSessionGuid;
        private readonly Action<string> _deleteDirectory;
        private readonly Action<string> _deleteFile;

        public HotReloadIntroducedTypeArtifactSweeper(
            string projectRoot,
            string currentSessionId,
            Action<string> deleteDirectory = null,
            Action<string> deleteFile = null)
        {
            // Why absolute only: a relative root resolves against whatever the working directory
            // happens to be, and the sweep deletes directories recursively below it.
            if (string.IsNullOrEmpty(projectRoot) || !Path.IsPathRooted(projectRoot))
            {
                throw new ArgumentException("The project root must be an absolute path.", nameof(projectRoot));
            }

            // Why a GUID only: session directories are told apart by parsing their names as GUIDs,
            // so an id of any other shape would make the current session look like an earlier one.
            if (!Guid.TryParseExact(currentSessionId, GuidFormat, out Guid currentSessionGuid))
            {
                throw new ArgumentException(
                    "The current session id must be a GUID in \"N\" format.",
                    nameof(currentSessionId));
            }

            _projectRoot = projectRoot;
            _currentSessionId = currentSessionId;
            _currentSessionGuid = currentSessionGuid;
            _deleteDirectory = deleteDirectory ?? DeleteDirectoryRecursively;
            _deleteFile = deleteFile ?? File.Delete;
        }

        /// <summary>
        /// Deletes every session directory named by a GUID other than the current session's, then
        /// every reference-cache copy of an artifact the current session does not hold.
        /// </summary>
        public void Sweep()
        {
            string artifactsRoot = Path.Combine(
                _projectRoot,
                HotReloadConstants.IntroducedTypeArtifactsRelativeDirectory);
            DeleteEarlierSessionDirectories(artifactsRoot);

            string currentSessionDirectory = Path.Combine(artifactsRoot, _currentSessionId);
            DeleteCopiesOfEarlierArtifacts(
                Path.Combine(_projectRoot, HotReloadConstants.PublicizedRefsRelativeDirectory),
                currentSessionDirectory);
            DeleteCopiesOfEarlierArtifacts(
                Path.Combine(_projectRoot, HotReloadConstants.InternalsExposedRefsRelativeDirectory),
                currentSessionDirectory);
        }

        private void DeleteEarlierSessionDirectories(string artifactsRoot)
        {
            foreach (string sessionDirectory in ListEntries(artifactsRoot, Directory.GetDirectories))
            {
                if (!IsEarlierSessionDirectory(Path.GetFileName(sessionDirectory)))
                {
                    continue;
                }

                TryDelete(sessionDirectory, _deleteDirectory);
            }
        }

        // Why compare parsed GUIDs: a name that differs from the current session id only in case
        // is the current session's directory on a case-insensitive file system.
        private bool IsEarlierSessionDirectory(string directoryName)
        {
            return Guid.TryParseExact(directoryName, GuidFormat, out Guid sessionGuid)
                && sessionGuid != _currentSessionGuid;
        }

        private void DeleteCopiesOfEarlierArtifacts(string cacheDirectory, string currentSessionDirectory)
        {
            foreach (string cachedFile in ListEntries(cacheDirectory, Directory.GetFiles))
            {
                if (!TryReadArtifactId(Path.GetFileName(cachedFile), out string artifactId))
                {
                    continue;
                }

                // Why only the current session decides: once the earlier sessions are gone it holds
                // every artifact this domain can still load, and a copy deleted too eagerly is
                // rebuilt on its next request, while an artifact directory cannot be.
                if (Directory.Exists(Path.Combine(currentSessionDirectory, artifactId)))
                {
                    continue;
                }

                TryDelete(cachedFile, _deleteFile);
            }
        }

        // Why read the id at a fixed offset: a copy is named <assembly name>-<Mvid>.dll, a temporary
        // file it is written through appends .tmp-<GUID>, and the assembly name is the prefix
        // followed by the 32-digit artifact id.
        private static bool TryReadArtifactId(string fileName, out string artifactId)
        {
            artifactId = null;
            string prefix = HotReloadConstants.IntroducedTypeArtifactAssemblyNamePrefix;
            int separatorIndex = prefix.Length + ArtifactIdLength;
            if (fileName.Length <= separatorIndex
                || !fileName.StartsWith(prefix, StringComparison.Ordinal)
                || fileName[separatorIndex] != ArtifactCopyNameSeparator)
            {
                return false;
            }

            string candidate = fileName.Substring(prefix.Length, ArtifactIdLength);
            if (!Guid.TryParseExact(candidate, GuidFormat, out Guid _))
            {
                return false;
            }

            artifactId = candidate;
            return true;
        }

        // Why swallow only these two, here and when deleting: the sweep only reclaims disk space,
        // so a directory it cannot read or an entry the OS keeps is left for the next domain to
        // retry rather than failing the Editor's first update.
        private string[] ListEntries(string directory, Func<string, string[]> list)
        {
            if (!Directory.Exists(directory))
            {
                return Array.Empty<string>();
            }

            try
            {
                return list(directory);
            }
            catch (IOException ex)
            {
                LogFailure(directory, ex.Message);
            }
            catch (UnauthorizedAccessException ex)
            {
                LogFailure(directory, ex.Message);
            }

            return Array.Empty<string>();
        }

        private void TryDelete(string path, Action<string> delete)
        {
            try
            {
                delete(path);
            }
            catch (IOException ex)
            {
                LogFailure(path, ex.Message);
            }
            catch (UnauthorizedAccessException ex)
            {
                LogFailure(path, ex.Message);
            }
        }

        private static void LogFailure(string path, string reason)
        {
            VibeLogger.LogWarning(
                HotReloadConstants.VibeLogIntroducedTypeArtifactSweepFailed,
                "An introduced-type artifact of an earlier domain could not be swept.",
                new { path, reason });
        }

        private static void DeleteDirectoryRecursively(string path)
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
