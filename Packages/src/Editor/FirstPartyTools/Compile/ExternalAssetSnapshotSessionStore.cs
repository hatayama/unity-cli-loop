using System;
using System.Collections.Generic;
using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Serializes tracked asset fingerprints so focus-return recovery survives editor domain reloads.
    /// </summary>
    internal static class ExternalAssetSnapshotSessionStore
    {
        internal static string SerializeSnapshots(
            Dictionary<string, (bool Exists, DateTime LastWriteTimeUtc, long Length)> snapshots)
        {
            Debug.Assert(snapshots != null, "snapshots must not be null");

            AssetSnapshotSessionData data = new AssetSnapshotSessionData();
            data.Entries = new AssetSnapshotEntry[snapshots.Count];
            int index = 0;
            foreach (KeyValuePair<string, (bool Exists, DateTime LastWriteTimeUtc, long Length)> snapshot in snapshots)
            {
                data.Entries[index] = new AssetSnapshotEntry
                {
                    AssetPath = snapshot.Key,
                    Exists = snapshot.Value.Exists,
                    LastWriteTimeUtcTicks = snapshot.Value.LastWriteTimeUtc.Ticks,
                    Length = snapshot.Value.Length
                };
                index++;
            }

            return JsonUtility.ToJson(data);
        }

        internal static void RestoreSnapshots(
            Dictionary<string, (bool Exists, DateTime LastWriteTimeUtc, long Length)> snapshots,
            string json)
        {
            Debug.Assert(snapshots != null, "snapshots must not be null");

            snapshots.Clear();
            if (string.IsNullOrEmpty(json))
            {
                return;
            }

            AssetSnapshotSessionData data = JsonUtility.FromJson<AssetSnapshotSessionData>(json);
            if (data == null || data.Entries == null)
            {
                return;
            }

            for (int i = 0; i < data.Entries.Length; i++)
            {
                AssetSnapshotEntry entry = data.Entries[i];
                if (entry == null || string.IsNullOrEmpty(entry.AssetPath))
                {
                    continue;
                }

                snapshots[entry.AssetPath] = (
                    entry.Exists,
                    new DateTime(entry.LastWriteTimeUtcTicks, DateTimeKind.Utc),
                    entry.Length);
            }
        }

        [Serializable]
        private sealed class AssetSnapshotSessionData
        {
            public AssetSnapshotEntry[] Entries = new AssetSnapshotEntry[0];
        }

        [Serializable]
        private sealed class AssetSnapshotEntry
        {
            public string AssetPath;
            public bool Exists;
            public long LastWriteTimeUtcTicks;
            public long Length;
        }
    }
}
