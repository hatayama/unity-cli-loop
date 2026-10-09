using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// The length and last write time each source had when its snapshot copy was taken, kept in
    /// the snapshot directory so a source that still has both is known to equal its copy without
    /// reading either file. A manifest that cannot be parsed in full answers no stamp at all.
    /// A copy of a source written since the compile started that did not match the PDB carries a
    /// mark; a marked copy is not the compiled source.
    /// </summary>
    internal sealed class HotReloadSourceStampManifest
    {
        internal static readonly HotReloadSourceStampManifest Empty =
            new HotReloadSourceStampManifest(new Dictionary<string, Stamp>(StringComparer.Ordinal));

        private readonly Dictionary<string, Stamp> _stamps;

        private HotReloadSourceStampManifest(Dictionary<string, Stamp> stamps)
        {
            _stamps = stamps;
        }

        internal int Count => _stamps.Count;

        /// <summary>
        /// Reads the manifest of a snapshot directory. A missing, truncated or malformed file
        /// answers <see cref="Empty"/> as a whole.
        /// </summary>
        /// <remarks>
        /// Why the whole file: a line that cannot be parsed leaves no way to tell which other
        /// lines are right, and a stamp trusted by mistake would skip the byte comparison.
        /// </remarks>
        internal static HotReloadSourceStampManifest Load(string snapshotDirectory)
        {
            Debug.Assert(!string.IsNullOrEmpty(snapshotDirectory), "snapshotDirectory must not be null or empty.");

            string path = Path.Combine(snapshotDirectory, HotReloadConstants.SourceStampManifestFileName);
            if (!File.Exists(path))
            {
                return Empty;
            }

            string[] lines = File.ReadAllText(path, Encoding.UTF8).Split('\n');
            for (int index = 0; index < lines.Length; index++)
            {
                lines[index] = lines[index].TrimEnd('\r');
            }

            // The header, every stamp line, and the empty string after the last "\n".
            if (lines.Length < 2
                || !string.Equals(lines[0], HotReloadConstants.SourceStampManifestHeader, StringComparison.Ordinal)
                || lines[lines.Length - 1].Length != 0)
            {
                return Empty;
            }

            Dictionary<string, Stamp> stamps = new Dictionary<string, Stamp>(lines.Length - 2, StringComparer.Ordinal);
            for (int index = 1; index < lines.Length - 1; index++)
            {
                if (!TryParseLine(lines[index], out string fileName, out Stamp stamp)
                    || stamps.ContainsKey(fileName))
                {
                    return Empty;
                }

                stamps.Add(fileName, stamp);
            }

            return new HotReloadSourceStampManifest(stamps);
        }

        /// <summary>
        /// Returns the stamp recorded for a snapshot file name, or false when the manifest has none.
        /// </summary>
        internal bool TryGetStamp(string snapshotFileName, out long length, out long lastWriteTimeUtcTicks)
        {
            Debug.Assert(!string.IsNullOrEmpty(snapshotFileName), "snapshotFileName must not be null or empty.");

            if (_stamps.TryGetValue(snapshotFileName, out Stamp stamp))
            {
                length = stamp.Length;
                lastWriteTimeUtcTicks = stamp.LastWriteTimeUtcTicks;
                return true;
            }

            length = 0;
            lastWriteTimeUtcTicks = 0;
            return false;
        }

        /// <summary>
        /// Returns whether the snapshot file's line is marked as edited after the compile.
        /// </summary>
        internal bool IsEditedAfterCompile(string snapshotFileName)
        {
            Debug.Assert(!string.IsNullOrEmpty(snapshotFileName), "snapshotFileName must not be null or empty.");

            return _stamps.TryGetValue(snapshotFileName, out Stamp stamp) && stamp.EditedAfterCompile;
        }

        /// <summary>
        /// Formats one manifest line: "&lt;file name&gt;\t&lt;length&gt;\t&lt;ticks&gt;\t&lt;0 or 1&gt;", where 1
        /// marks a copy edited after the compile. The file name is a hex hash plus ".cs", so it never
        /// holds a TAB or a line break.
        /// </summary>
        internal static string FormatLine(
            string snapshotFileName,
            long length,
            long lastWriteTimeUtcTicks,
            bool editedAfterCompile)
        {
            Debug.Assert(!string.IsNullOrEmpty(snapshotFileName), "snapshotFileName must not be null or empty.");
            Debug.Assert(length >= 0, "length must not be negative.");
            Debug.Assert(lastWriteTimeUtcTicks >= 0, "lastWriteTimeUtcTicks must not be negative.");

            return snapshotFileName
                + "\t" + length.ToString(CultureInfo.InvariantCulture)
                + "\t" + lastWriteTimeUtcTicks.ToString(CultureInfo.InvariantCulture)
                + "\t" + (editedAfterCompile ? "1" : "0");
        }

        /// <summary>
        /// Writes the manifest into a snapshot directory that is not published yet (the capture's
        /// temporary directory); the directory move publishes it together with the copies.
        /// </summary>
        internal static void Write(string snapshotDirectory, IReadOnlyList<string> lines)
        {
            Debug.Assert(Directory.Exists(snapshotDirectory), "snapshotDirectory must exist.");
            Debug.Assert(lines != null, "lines must not be null.");

            StringBuilder text = new StringBuilder();
            text.Append(HotReloadConstants.SourceStampManifestHeader).Append('\n');
            for (int index = 0; index < lines.Count; index++)
            {
                text.Append(lines[index]).Append('\n');
            }

            File.WriteAllText(
                Path.Combine(snapshotDirectory, HotReloadConstants.SourceStampManifestFileName),
                text.ToString(),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        // Why NumberStyles.None: it rejects a sign, white space and separators, so "-1" or " 12"
        // makes the line malformed rather than a stamp no source can have.
        private static bool TryParseLine(string line, out string fileName, out Stamp stamp)
        {
            fileName = null;
            stamp = default;
            string[] fields = line.Split('\t');
            if (fields.Length != 4 || fields[0].Length == 0)
            {
                return false;
            }

            if (!long.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out long length))
            {
                return false;
            }

            if (!long.TryParse(fields[2], NumberStyles.None, CultureInfo.InvariantCulture, out long ticks))
            {
                return false;
            }

            bool editedAfterCompile;
            if (string.Equals(fields[3], "1", StringComparison.Ordinal))
            {
                editedAfterCompile = true;
            }
            else if (string.Equals(fields[3], "0", StringComparison.Ordinal))
            {
                editedAfterCompile = false;
            }
            else
            {
                return false;
            }

            fileName = fields[0];
            stamp = new Stamp(length, ticks, editedAfterCompile);
            return true;
        }

        private readonly struct Stamp
        {
            internal readonly long Length;
            internal readonly long LastWriteTimeUtcTicks;
            internal readonly bool EditedAfterCompile;

            internal Stamp(long length, long lastWriteTimeUtcTicks, bool editedAfterCompile)
            {
                Length = length;
                LastWriteTimeUtcTicks = lastWriteTimeUtcTicks;
                EditedAfterCompile = editedAfterCompile;
            }
        }
    }
}
