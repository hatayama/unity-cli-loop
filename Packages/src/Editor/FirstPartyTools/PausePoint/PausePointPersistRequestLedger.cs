using System;
using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// In-domain record of which pause points were enabled with --persist, so the reload hook can
    /// write the still-armed ones out just before the domain goes away.
    /// </summary>
    internal static class PausePointPersistRequestLedger
    {
        // Registration order is the order the user armed the pause points in, and the re-arm has
        // to replay it, so the list carries the order and the dictionary carries the lookup.
        private static readonly List<PausePointPersistedRecord> Records = new();
        private static readonly Dictionary<string, int> IndexByRegistryId = new(StringComparer.Ordinal);

        public static void Upsert(PausePointPersistedRecord record)
        {
            if (record == null)
            {
                throw new ArgumentNullException(nameof(record));
            }

            if (string.IsNullOrEmpty(record.RegistryId))
            {
                throw new ArgumentException("RegistryId must not be empty.", nameof(record));
            }

            if (IndexByRegistryId.TryGetValue(record.RegistryId, out int index))
            {
                // A re-enable keeps its original position: the user did not re-order the pause
                // points, they replaced one request's arguments.
                Records[index] = record;
                return;
            }

            IndexByRegistryId[record.RegistryId] = Records.Count;
            Records.Add(record);
        }

        public static void Remove(string registryId)
        {
            if (string.IsNullOrEmpty(registryId) || !IndexByRegistryId.TryGetValue(registryId, out int index))
            {
                return;
            }

            Records.RemoveAt(index);
            IndexByRegistryId.Remove(registryId);
            ReindexFrom(index);
        }

        public static IReadOnlyList<PausePointPersistedRecord> CollectArmed(Func<string, bool> isArmed)
        {
            if (isArmed == null)
            {
                throw new ArgumentNullException(nameof(isArmed));
            }

            List<PausePointPersistedRecord> armed = new(Records.Count);
            foreach (PausePointPersistedRecord record in Records)
            {
                if (isArmed(record.RegistryId))
                {
                    armed.Add(record);
                }
            }

            return armed;
        }

        internal static void ResetForTests()
        {
            Records.Clear();
            IndexByRegistryId.Clear();
        }

        private static void ReindexFrom(int index)
        {
            for (int i = index; i < Records.Count; i++)
            {
                IndexByRegistryId[Records[i].RegistryId] = i;
            }
        }
    }
}
