using System;
using System.Collections.Generic;

using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Decides which group plans hold nothing but inputs that were short-circuited as already
    /// active, and fills those inputs' results once the changed groups have run.
    /// </summary>
    internal sealed class HotReloadDeferredInputClassifier
    {
        internal IReadOnlyList<HotReloadDeferredInputPlan> ClassifyAllDeferredPlans(
            IReadOnlyList<HotReloadFileGroupPlan> plans,
            HotReloadInputResolutionSlot[] slots)
        {
            Debug.Assert(plans != null, "plans must not be null.");
            Debug.Assert(slots != null, "slots must not be null.");

            List<HotReloadDeferredInputPlan> classified = new List<HotReloadDeferredInputPlan>(plans.Count);
            for (int planIndex = 0; planIndex < plans.Count; planIndex++)
            {
                classified.Add(
                    new HotReloadDeferredInputPlan(
                        planIndex,
                        AreAllInputsDeferredAlreadyActive(plans[planIndex], slots)));
            }

            return classified;
        }

        // Why every all-deferred plan of the assembly: a repeated path can open an
        // all-deferred plan before the last changed group, and that plan can hold a
        // caller that sibling auto-include cannot add because it is already in pathsInRun.
        internal void AppendUniqueDeferredInputIndexes(
            IReadOnlyList<HotReloadFileGroupPlan> plans,
            IReadOnlyList<HotReloadDeferredInputPlan> classified,
            int currentPlanIndex,
            HotReloadInputResolutionSlot[] slots,
            List<int> inputIndexes)
        {
            Debug.Assert(plans != null, "plans must not be null.");
            Debug.Assert(classified != null, "classified must not be null.");
            Debug.Assert(slots != null, "slots must not be null.");
            Debug.Assert(inputIndexes != null, "inputIndexes must not be null.");

            string assemblyName = plans[currentPlanIndex].AssemblyName;
            HashSet<string> pathsInGroup = new HashSet<string>(
                HotReloadSourcePathNormalizer.ProjectRelativePathComparer());
            for (int position = 0; position < inputIndexes.Count; position++)
            {
                pathsInGroup.Add(slots[inputIndexes[position]].GroupFile.ProjectRelativePath);
            }

            for (int planIndex = 0; planIndex < plans.Count; planIndex++)
            {
                if (planIndex == currentPlanIndex
                    || !classified[planIndex].IsAllDeferred
                    || !string.Equals(
                        plans[planIndex].AssemblyName,
                        assemblyName,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                IReadOnlyList<int> deferredIndexes = plans[planIndex].InputIndexes;
                for (int deferredPosition = 0; deferredPosition < deferredIndexes.Count; deferredPosition++)
                {
                    int inputIndex = deferredIndexes[deferredPosition];
                    string path = slots[inputIndex].GroupFile.ProjectRelativePath;
                    if (pathsInGroup.Add(path))
                    {
                        inputIndexes.Add(inputIndex);
                    }
                }
            }
        }

        internal void ApplyDeferredAlreadyActive(
            HotReloadFileGroupPlan plan,
            HotReloadInputResolutionSlot[] slots)
        {
            foreach (int inputIndex in plan.InputIndexes)
            {
                HotReloadInputResolutionSlot slot = slots[inputIndex];
                if (slot.Result != null)
                {
                    continue;
                }

                Debug.Assert(
                    slot.DeferredAlreadyActive != null,
                    "An unfilled deferred slot must have AlreadyActive rows.");
                slot.GroupFile.Sinks.Outcomes.AddRange(slot.DeferredAlreadyActive);
                slot.Result = new HotReloadFileProcessResult(
                    slot.GroupFile.Sinks.Outcomes,
                    slot.GroupFile.Sinks.Warnings,
                    0);
            }
        }

        private bool AreAllInputsDeferredAlreadyActive(
            HotReloadFileGroupPlan plan,
            HotReloadInputResolutionSlot[] slots)
        {
            foreach (int inputIndex in plan.InputIndexes)
            {
                if (slots[inputIndex].DeferredAlreadyActive == null)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
