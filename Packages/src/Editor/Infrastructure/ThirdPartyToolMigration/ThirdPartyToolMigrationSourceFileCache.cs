using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Caches source text within one preview pass so analysis phases share a single disk read per file.
    /// </summary>
    internal sealed class ThirdPartyToolMigrationSourceFileCache
    {
        private readonly Func<string, string> _readAllText;
        private readonly Dictionary<string, string> _sources = new(StringComparer.Ordinal);

        internal ThirdPartyToolMigrationSourceFileCache()
            : this(ThirdPartyToolMigrationFileAccess.ReadAllText)
        {
        }

        internal ThirdPartyToolMigrationSourceFileCache(Func<string, string> readAllText)
        {
            Debug.Assert(readAllText != null, "readAllText must not be null");

            _readAllText = readAllText ?? throw new ArgumentNullException(nameof(readAllText));
        }

        internal string ReadAllText(string filePath)
        {
            Debug.Assert(!string.IsNullOrEmpty(filePath), "filePath must not be null or empty");

            if (_sources.TryGetValue(filePath, out string source))
            {
                return source;
            }

            string loadedSource = _readAllText(filePath);
            _sources.Add(filePath, loadedSource);
            return loadedSource;
        }
    }
}
