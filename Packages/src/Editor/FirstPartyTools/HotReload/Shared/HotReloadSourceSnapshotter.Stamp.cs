using System.IO;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    // The stamp beside an assembly's snapshot directories records the compiled DLL the snapshot was
    // taken from, so a reload whose DLL has not changed skips the capture.
    internal static partial class HotReloadSourceSnapshotter
    {
        private const string StampFileExtension = ".stamp";

        private static string StampPath(string snapshotRoot, string assemblyName)
        {
            return Path.Combine(snapshotRoot, assemblyName + StampFileExtension);
        }

        private static bool HasMatchingStamp(string stampPath, long dllMtimeTicks, long dllByteLength)
        {
            if (!File.Exists(stampPath))
            {
                return false;
            }

            string stampText = File.ReadAllText(stampPath).Trim();
            string[] parts = stampText.Split(',');
            if (parts.Length != 3)
            {
                return false;
            }

            if (string.IsNullOrEmpty(parts[0]))
            {
                return false;
            }

            if (!long.TryParse(parts[1], out long stampedMtimeTicks)
                || !long.TryParse(parts[2], out long stampedByteLength))
            {
                return false;
            }

            return stampedMtimeTicks == dllMtimeTicks && stampedByteLength == dllByteLength;
        }

        private static void WriteStamp(string stampPath, string mvid, long dllMtimeTicks, long dllByteLength)
        {
            File.WriteAllText(stampPath, mvid + "," + dllMtimeTicks + "," + dllByteLength);
        }
    }
}
