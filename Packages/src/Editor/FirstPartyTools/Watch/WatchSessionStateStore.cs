using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Keeps watch expressions in SessionState, the Editor-local store that survives a domain
    /// reload and is dropped when the Editor process exits.
    /// </summary>
    internal sealed class WatchSessionStateStore : IWatchPersistenceStore
    {
        private const string RecordsKey = "io.github.hatayama.uloopmcp.watch.persistedRecords";

        public IReadOnlyList<WatchPersistedRecord> Load()
        {
            string json = SessionState.GetString(RecordsKey, string.Empty);
            if (string.IsNullOrEmpty(json))
            {
                return Array.Empty<WatchPersistedRecord>();
            }

            WatchPersistedRecordList parsed = TryParse(json);
            if (parsed?.Records == null)
            {
                return Array.Empty<WatchPersistedRecord>();
            }

            List<WatchPersistedRecord> records = new(parsed.Records.Count);
            foreach (WatchPersistedRecord record in parsed.Records)
            {
                // A record without an expression cannot be recompiled, so it is not a watch to restore.
                if (record == null || string.IsNullOrEmpty(record.Id) || string.IsNullOrEmpty(record.Expression))
                {
                    continue;
                }

                records.Add(record);
            }

            return records;
        }

        public void Save(IReadOnlyList<WatchPersistedRecord> records)
        {
            if (records == null)
            {
                throw new ArgumentNullException(nameof(records));
            }

            WatchPersistedRecordList payload = new();
            foreach (WatchPersistedRecord record in records)
            {
                if (record == null || string.IsNullOrEmpty(record.Id) || string.IsNullOrEmpty(record.Expression))
                {
                    throw new ArgumentException(
                        "Every persisted watch record must carry a non-empty Id and Expression.",
                        nameof(records));
                }

                payload.Records.Add(record);
            }

            SessionState.SetString(RecordsKey, JsonUtility.ToJson(payload));
        }

        // Why swallow: a malformed value can only come from a store written by another uloop
        // generation, and losing the restore is better than blocking Editor startup on it.
        private static WatchPersistedRecordList TryParse(string json)
        {
            try
            {
                return JsonUtility.FromJson<WatchPersistedRecordList>(json);
            }
            catch (ArgumentException exception)
            {
                VibeLogger.LogWarning(
                    "watch_persisted_records_parse_failed",
                    "Stored watch expressions could not be parsed and were dropped.",
                    new { error = exception.Message });
                return null;
            }
        }
    }
}
