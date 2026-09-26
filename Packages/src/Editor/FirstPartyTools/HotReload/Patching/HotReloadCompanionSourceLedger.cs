using System;
using System.Collections.Generic;

using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Unchanged files an earlier reload was given beside the files it applied, with the source
    /// hash each was given at. A later reload of the same assembly brings them back, so the bodies
    /// it re-applies still bind the way the reload that listed them did.
    /// </summary>
    /// <remarks>
    /// Why the files are remembered at all: such a file leaves no patch and no added member, so no
    /// other record names it, yet an added method compiled without it can bind against a type the
    /// reload declares from source instead of the compiled one a compiled signature names.
    /// Why a revert-all keeps them: the revert drops the patches that needed them, and a reload that
    /// applies those patches again needs them again.
    /// </remarks>
    internal sealed class HotReloadCompanionSourceLedger
    {
        internal const char FieldSeparator = '\t';

        internal const char LineSeparator = '\n';

        private readonly Dictionary<string, string> _hashByPath =
            new Dictionary<string, string>(HotReloadSourcePathNormalizer.ProjectRelativePathComparer());

        internal void Record(string projectRelativePath, string sourceContentSha256)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRelativePath), "projectRelativePath must not be empty.");
            Debug.Assert(!string.IsNullOrEmpty(sourceContentSha256), "sourceContentSha256 must not be empty.");

            _hashByPath[projectRelativePath] = sourceContentSha256;
        }

        internal void Remove(string projectRelativePath)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRelativePath), "projectRelativePath must not be empty.");

            _hashByPath.Remove(projectRelativePath);
        }

        /// <summary>The hash the file was last given at, or null when it is not a companion.</summary>
        internal string TryGetHash(string projectRelativePath)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRelativePath), "projectRelativePath must not be empty.");

            return _hashByPath.TryGetValue(projectRelativePath, out string hash) ? hash : null;
        }

        internal IReadOnlyList<string> ListPaths()
        {
            return new List<string>(_hashByPath.Keys);
        }

        internal void Clear()
        {
            _hashByPath.Clear();
        }

        /// <summary>One "project-relative path&lt;TAB&gt;hash" line per file, sorted ordinal.</summary>
        internal string Serialize()
        {
            List<string> lines = new List<string>(_hashByPath.Count);
            foreach (KeyValuePair<string, string> pair in _hashByPath)
            {
                lines.Add(pair.Key + FieldSeparator + pair.Value);
            }

            lines.Sort(StringComparer.Ordinal);
            return string.Join(LineSeparator.ToString(), lines);
        }

        /// <summary>Replaces the contents with what <see cref="Serialize"/> wrote.</summary>
        internal void Restore(string serialized)
        {
            _hashByPath.Clear();
            if (string.IsNullOrEmpty(serialized))
            {
                return;
            }

            string[] lines = serialized.Split(LineSeparator);
            for (int index = 0; index < lines.Length; index++)
            {
                if (string.IsNullOrEmpty(lines[index]))
                {
                    continue;
                }

                string[] parts = lines[index].Split(FieldSeparator);
                // Only Serialize writes this text, and neither a path nor a hash holds a tab, so
                // any other shape is corrupted state a path read from it cannot be trusted from.
                if (parts.Length != 2 || parts[0].Length == 0 || parts[1].Length == 0)
                {
                    throw new InvalidOperationException(
                        "A companion source line must hold one path and one hash: " + lines[index]);
                }

                _hashByPath[parts[0]] = parts[1];
            }
        }
    }
}
