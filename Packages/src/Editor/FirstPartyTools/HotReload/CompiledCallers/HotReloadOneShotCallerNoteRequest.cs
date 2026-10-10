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

        public HotReloadCompiledMethodIdentity Identity { get; }

        public string Method { get; }
    }
}
