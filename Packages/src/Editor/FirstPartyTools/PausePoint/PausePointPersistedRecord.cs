using System;
using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// One persisted enable request, holding everything needed to re-issue it after a domain
    /// reload. The evaluated pause point itself cannot survive the reload, so the request is
    /// replayed instead of the state being restored.
    /// </summary>
    [Serializable]
    internal sealed class PausePointPersistedRecord
    {
        // The id the registry actually stores the entry under: the schema Id for an --id pause
        // point, and the normalized "file:line" id for a source pause point. This is what
        // IsArmed has to be asked about, and it is not always the schema's Id.
        public string RegistryId;

        public string Id;
        public string File;
        public int Line;
        public int TimeoutSeconds;
        public string Mode;
        public int MaxHistory;
        public string HitWhen;
        public int MaxPreviewElements;
        public int MaxCallerFrames;
        public string Method;
        public string SnapshotTiming;

        public static PausePointPersistedRecord FromSchema(string registryId, EnablePausePointSchema parameters)
        {
            return new PausePointPersistedRecord
            {
                RegistryId = registryId,
                Id = parameters.Id,
                File = parameters.File,
                Line = parameters.Line,
                TimeoutSeconds = parameters.TimeoutSeconds,
                Mode = parameters.Mode,
                MaxHistory = parameters.MaxHistory,
                HitWhen = parameters.HitWhen,
                MaxPreviewElements = parameters.MaxPreviewElements,
                MaxCallerFrames = parameters.MaxCallerFrames,
                Method = parameters.Method,
                SnapshotTiming = parameters.SnapshotTiming
            };
        }

        public EnablePausePointSchema ToSchema()
        {
            // A source pause point keeps its Id empty: PausePointEnableValidation rejects an
            // enable that carries both an Id and a File/Line, and RegistryId is this ledger's
            // key rather than a request field.
            return new EnablePausePointSchema
            {
                Id = string.IsNullOrEmpty(File) ? Id : string.Empty,
                File = File,
                Line = Line,
                TimeoutSeconds = TimeoutSeconds,
                Mode = Mode,
                MaxHistory = MaxHistory,
                HitWhen = HitWhen,
                MaxPreviewElements = MaxPreviewElements,
                MaxCallerFrames = MaxCallerFrames,
                Method = Method,
                SnapshotTiming = SnapshotTiming,
                Persist = true
            };
        }
    }

    /// <summary>
    /// JsonUtility needs a concrete wrapper type to serialize a list of records.
    /// </summary>
    [Serializable]
    internal sealed class PausePointPersistedRecordList
    {
        public List<PausePointPersistedRecord> Records = new();
    }
}
