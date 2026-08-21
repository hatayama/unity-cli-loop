using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Renders auto-injected using hints, split by speculative pre-injection vs retry resolution.
    /// </summary>
    internal static class AutoInjectedNamespaceHintBuilder
    {
        internal static List<string> BuildHints(IReadOnlyList<AutoInjectedNamespace> autoInjectedNamespaces)
        {
            Debug.Assert(autoInjectedNamespaces != null, "autoInjectedNamespaces must not be null.");

            List<AutoInjectedNamespace> retryResolved = new();
            List<AutoInjectedNamespace> speculative = new();
            foreach (AutoInjectedNamespace item in autoInjectedNamespaces)
            {
                if (item.IsSpeculative)
                {
                    speculative.Add(item);
                    continue;
                }

                retryResolved.Add(item);
            }

            List<string> hints = new();
            AddHintIfAny(
                hints,
                retryResolved,
                DynamicCodeConstants.RetryResolvedUsingHintFormat);
            AddHintIfAny(
                hints,
                speculative,
                DynamicCodeConstants.SpeculativeUsingHintFormat);
            return hints;
        }

        private static void AddHintIfAny(
            List<string> hints,
            List<AutoInjectedNamespace> items,
            string format)
        {
            if (items.Count == 0)
            {
                return;
            }

            hints.Add(string.Format(format, items.Count, FormatUsingList(items)));
        }

        private static string FormatUsingList(List<AutoInjectedNamespace> items)
        {
            StringBuilder builder = new();
            for (int index = 0; index < items.Count; index++)
            {
                if (index > 0)
                {
                    builder.Append(' ');
                }

                builder.Append(
                    string.Format(
                        DynamicCodeConstants.UsingAttributionItemFormat,
                        items[index].Namespace,
                        items[index].TriggerIdentifier));
            }

            return builder.ToString();
        }
    }
}
