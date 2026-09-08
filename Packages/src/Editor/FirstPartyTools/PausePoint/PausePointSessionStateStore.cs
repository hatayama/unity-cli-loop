using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Keeps persisted enable requests in SessionState, the Editor-local store that survives a
    /// domain reload and is dropped when the Editor process exits.
    /// </summary>
    internal sealed class PausePointSessionStateStore : IPausePointPersistenceStore
    {
        private const string RecordsKey = "io.github.hatayama.uloopmcp.pausePoint.persistedRecords";

        public IReadOnlyList<PausePointPersistedRecord> Load()
        {
            string json = SessionState.GetString(RecordsKey, string.Empty);
            if (string.IsNullOrEmpty(json))
            {
                return Array.Empty<PausePointPersistedRecord>();
            }

            PausePointPersistedRecordList parsed = TryParse(json);
            if (parsed?.Records == null)
            {
                return Array.Empty<PausePointPersistedRecord>();
            }

            List<PausePointPersistedRecord> records = new(parsed.Records.Count);
            foreach (PausePointPersistedRecord record in parsed.Records)
            {
                // Without a registry id there is nothing to re-arm or to key the ledger on.
                if (record == null || string.IsNullOrEmpty(record.RegistryId))
                {
                    continue;
                }

                records.Add(record);
            }

            return records;
        }

        public void Save(IReadOnlyList<PausePointPersistedRecord> records)
        {
            if (records == null)
            {
                throw new ArgumentNullException(nameof(records));
            }

            PausePointPersistedRecordList payload = new();
            foreach (PausePointPersistedRecord record in records)
            {
                if (record == null || string.IsNullOrEmpty(record.RegistryId))
                {
                    throw new ArgumentException(
                        "Every persisted pause point record must carry a non-empty RegistryId.",
                        nameof(records));
                }

                payload.Records.Add(record);
            }

            SessionState.SetString(RecordsKey, JsonUtility.ToJson(payload));
        }

        // Why swallow: a malformed value can only come from a store written by another uloop
        // generation, and losing the re-arm is better than blocking Editor startup on it.
        private static PausePointPersistedRecordList TryParse(string json)
        {
            try
            {
                return JsonUtility.FromJson<PausePointPersistedRecordList>(json);
            }
            catch (ArgumentException exception)
            {
                VibeLogger.LogWarning(
                    "pause_point_persisted_records_parse_failed",
                    "Stored pause point enable requests could not be parsed and were dropped.",
                    new { error = exception.Message });
                return null;
            }
        }
    }
}
