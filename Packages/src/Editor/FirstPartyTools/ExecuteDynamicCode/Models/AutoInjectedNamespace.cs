using System.Diagnostics;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// One auto-injected using directive and the identifier that first caused it to be added.
    /// </summary>
    public sealed class AutoInjectedNamespace
    {
        public AutoInjectedNamespace(string namespaceName, string triggerIdentifier, bool isSpeculative)
        {
            Debug.Assert(!string.IsNullOrEmpty(namespaceName), "namespaceName must not be empty.");
            Debug.Assert(!string.IsNullOrEmpty(triggerIdentifier), "triggerIdentifier must not be empty.");
            Namespace = namespaceName;
            TriggerIdentifier = triggerIdentifier;
            IsSpeculative = isSpeculative;
        }

        public string Namespace { get; }

        public string TriggerIdentifier { get; }

        public bool IsSpeculative { get; }
    }
}
