using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// One method a one-shot lifecycle note is asked for: the compiled identity its callers are
    /// searched by and the name the note shows.
    /// </summary>
    public sealed class HotReloadOneShotCallerNoteRequest
    {
        public HotReloadOneShotCallerNoteRequest(HotReloadCompiledMethodIdentity identity, string method)
        {
            Debug.Assert(!string.IsNullOrEmpty(method), "method must not be null or empty.");

            Identity = identity;
            Method = method;
        }

        // Only the analysis behind the entry point reads these; hot-reload code outside it only
        // builds this value and passes it in.
        internal HotReloadCompiledMethodIdentity Identity { get; }

        internal string Method { get; }
    }
}
