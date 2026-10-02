using System;
using System.Collections.Generic;
using System.Globalization;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Caller facts used to determine whether a patched method has only one-shot lifecycle callers.
    /// </summary>
    internal readonly struct OneShotCallerClassification
    {
        public readonly string MethodName;
        public readonly bool IsOneShotLifecycleMessage;

        public OneShotCallerClassification(string methodName, bool isOneShotLifecycleMessage)
        {
            MethodName = methodName;
            IsOneShotLifecycleMessage = isOneShotLifecycleMessage;
        }
    }

    /// <summary>
    /// Builds caller-aware lifecycle notes from pre-classified compiled call sites.
    /// </summary>
    internal static class HotReloadOneShotCallerNoteBuilder
    {
        // Why conditional wording: apply time cannot know which objects have already run their lifecycle methods.
        public const string IndirectFormat =
            "{0} is called only from one-shot lifecycle method(s) ({1}) in the compiled assemblies; "
            + "objects that already ran them will not run the patched body. It takes effect only for "
            + "newly created objects, or run `uloop compile` and re-enter Play Mode. Callers hot reload "
            + "added or patched are not counted; if one of them calls it, the patched body already runs.";

        // Why only when OnEnable or OnDisable is a caller: toggling enabled makes Unity call those
        // two again on a live object, while Awake and Start never run again that way.
        public const string ReEnableSuffix =
            " To run it on live objects from OnEnable or OnDisable, set the component's `enabled` to "
            + "false and back to true (for example with `uloop execute-dynamic-code`); Awake and Start "
            + "do not run again that way.";

        /// <summary>
        /// Returns a note only when every compiled caller is a one-shot lifecycle message.
        /// </summary>
        public static string Build(
            string targetMethodName,
            IReadOnlyList<OneShotCallerClassification> callers)
        {
            if (callers.Count == 0)
            {
                return null;
            }

            List<string> lifecycleNames = new List<string>();
            foreach (OneShotCallerClassification caller in callers)
            {
                if (!caller.IsOneShotLifecycleMessage)
                {
                    return null;
                }

                if (!lifecycleNames.Contains(caller.MethodName))
                {
                    lifecycleNames.Add(caller.MethodName);
                }
            }

            lifecycleNames.Sort(StringComparer.Ordinal);
            string note = string.Format(
                CultureInfo.InvariantCulture,
                IndirectFormat,
                targetMethodName,
                string.Join(", ", lifecycleNames));
            bool reEnableRunsIt = lifecycleNames.Contains("OnEnable") || lifecycleNames.Contains("OnDisable");
            return reEnableRunsIt ? note + ReEnableSuffix : note;
        }
    }
}
