using System;
using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// One watch expression reduced to what a domain reload can carry: the compiled evaluator is
    /// an in-memory assembly that cannot be serialized, so only the inputs needed to compile it
    /// again are stored.
    /// </summary>
    [Serializable]
    internal sealed class WatchPersistedRecord
    {
        public string Id;
        public string Expression;
        public int MaxHistory;
    }

    /// <summary>
    /// JsonUtility serializes only classes with fields, so the record collection needs this wrapper.
    /// </summary>
    [Serializable]
    internal sealed class WatchPersistedRecordList
    {
        public List<WatchPersistedRecord> Records = new();
    }
}
