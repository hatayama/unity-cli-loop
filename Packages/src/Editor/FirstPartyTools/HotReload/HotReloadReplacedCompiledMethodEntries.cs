using System;
using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Indexes the worker entries that replace a compiled method (a return-type change) by the
    /// wire key of the signature they replace. Shared so that every caller agrees on what counts
    /// as a replacement rather than a deletion.
    /// </summary>
    internal static class HotReloadReplacedCompiledMethodEntries
    {
        // Why first-wins: duplicate keys would mean two entries claim the same compiled signature,
        // and the recorder's original linear scan reported the first match.
        public static IReadOnlyDictionary<string, TransformWorkerEntryDto> IndexByReplacedWireKey(
            IReadOnlyList<TransformWorkerEntryDto> entries)
        {
            Dictionary<string, TransformWorkerEntryDto> entriesByWireKey =
                new Dictionary<string, TransformWorkerEntryDto>(StringComparer.Ordinal);
            if (entries == null)
            {
                return entriesByWireKey;
            }

            for (int index = 0; index < entries.Count; index++)
            {
                TransformWorkerEntryDto entry = entries[index];
                if (entry == null || !entry.replacesCompiledMethod)
                {
                    continue;
                }

                string wireKey = HotReloadMethodKeys.BuildMethodKey(entry);
                if (entriesByWireKey.ContainsKey(wireKey))
                {
                    continue;
                }

                entriesByWireKey.Add(wireKey, entry);
            }

            return entriesByWireKey;
        }
    }
}
