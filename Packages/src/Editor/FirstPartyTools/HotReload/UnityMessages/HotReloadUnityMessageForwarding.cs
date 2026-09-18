using System;
using System.Collections.Generic;
using System.Reflection;

using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Keeps the proxies in step with the domain: it reads which added methods are Unity messages
    /// right now and tells the attacher what to bind, unbind, or leave alone.
    /// </summary>
    /// <remarks>
    /// Why the domain is re-read rather than told what changed: a group commit, a re-applied file,
    /// a revert, and a run cancelled halfway all leave the domain as the one truthful record, and
    /// reconciling from it means the next tick agrees with it no matter which path got there.
    /// </remarks>
    internal sealed class HotReloadUnityMessageForwarding
    {
        private readonly HotReloadDomain _domain;
        private readonly HotReloadUnityMessageProxyAttacher _attacher;
        // The shim methods each target type was last bound with, compared by reference: a re-applied
        // edit produces new MethodInfo objects even when the method keys are unchanged, and that is
        // exactly the case a rebuild is needed for.
        private readonly Dictionary<Type, MethodInfo[]> _boundShimsByTarget =
            new Dictionary<Type, MethodInfo[]>();
        // The shim methods that already failed to build, so a failure is reported once instead of
        // on every editor update until the user edits the file again.
        private readonly Dictionary<Type, MethodInfo[]> _failedShimsByTarget =
            new Dictionary<Type, MethodInfo[]>();

        internal HotReloadUnityMessageForwarding(
            HotReloadDomain domain,
            HotReloadUnityMessageProxyAttacher attacher)
        {
            Debug.Assert(domain != null, "domain must not be null.");
            Debug.Assert(attacher != null, "attacher must not be null.");
            _domain = domain;
            _attacher = attacher;
        }

        /// <summary>
        /// Brings the bindings up to date with the domain, adding a line to
        /// <paramref name="warnings"/> for every target type whose proxy could not be built.
        /// </summary>
        internal void Reconcile(List<string> warnings)
        {
            Dictionary<Type, MethodInfo[]> grouped = GroupForwardedShims();
            UnbindTypesNoLongerForwarded(grouped);
            foreach (KeyValuePair<Type, MethodInfo[]> pair in grouped)
            {
                ReconcileTargetType(pair.Key, pair.Value, warnings);
            }
        }

        /// <summary>Reconciles and then attaches; the editor update calls this.</summary>
        internal void Tick()
        {
            Reconcile(null);
            _attacher.Tick();
        }

        /// <summary>Takes every proxy off and forgets what was bound.</summary>
        internal void Clear()
        {
            _attacher.Clear();
            _boundShimsByTarget.Clear();
            _failedShimsByTarget.Clear();
        }

        internal void Suspend()
        {
            _attacher.Suspend();
        }

        internal void Resume()
        {
            _attacher.Resume();
        }

        private Dictionary<Type, MethodInfo[]> GroupForwardedShims()
        {
            Dictionary<Type, List<MethodInfo>> shimsByTarget = new Dictionary<Type, List<MethodInfo>>();
            // The domain hands them back sorted by method key, which is what makes the reference
            // comparison below stable across reconciles that changed nothing.
            foreach (HotReloadAddedMemberInfo member in _domain.DescribeAddedMembers())
            {
                MethodInfo shim = member.ShimMethod;
                if (shim == null)
                {
                    continue;
                }

                HotReloadUnityMessageDetector.Classification classification =
                    HotReloadUnityMessageDetector.Classify(shim, out Type targetType);
                if (classification != HotReloadUnityMessageDetector.Classification.Forwarded)
                {
                    continue;
                }

                if (!shimsByTarget.TryGetValue(targetType, out List<MethodInfo> shims))
                {
                    shims = new List<MethodInfo>();
                    shimsByTarget.Add(targetType, shims);
                }

                shims.Add(shim);
            }

            Dictionary<Type, MethodInfo[]> grouped = new Dictionary<Type, MethodInfo[]>();
            foreach (KeyValuePair<Type, List<MethodInfo>> pair in shimsByTarget)
            {
                grouped.Add(pair.Key, pair.Value.ToArray());
            }

            return grouped;
        }

        private void UnbindTypesNoLongerForwarded(Dictionary<Type, MethodInfo[]> grouped)
        {
            List<Type> gone = new List<Type>();
            foreach (KeyValuePair<Type, MethodInfo[]> pair in _boundShimsByTarget)
            {
                if (!grouped.ContainsKey(pair.Key))
                {
                    gone.Add(pair.Key);
                }
            }

            foreach (Type targetType in gone)
            {
                _attacher.Unbind(targetType);
                _boundShimsByTarget.Remove(targetType);
                _failedShimsByTarget.Remove(targetType);
            }
        }

        private void ReconcileTargetType(Type targetType, MethodInfo[] shims, List<string> warnings)
        {
            if (SameReferences(Lookup(_boundShimsByTarget, targetType), shims))
            {
                return;
            }

            if (SameReferences(Lookup(_failedShimsByTarget, targetType), shims))
            {
                return;
            }

            if (!TryBind(targetType, shims, out string failure))
            {
                // The type keeps nothing until the user edits it again, so a failed rebuild never
                // silently leaves the previous messages running against the new shims.
                _attacher.Unbind(targetType);
                _boundShimsByTarget.Remove(targetType);
                _failedShimsByTarget[targetType] = shims;
                warnings?.Add(
                    $"Unity message forwarding for {targetType.FullName} is unavailable: {failure}");
                return;
            }

            _boundShimsByTarget[targetType] = shims;
            _failedShimsByTarget.Remove(targetType);
        }

        // The only place this feature catches: building the forwarders and emitting the proxy type
        // run the runtime's own IL checks over shapes that came from the user's edit, and a refusal
        // has to become one warning rather than take the whole run down.
        private bool TryBind(Type targetType, MethodInfo[] shims, out string failure)
        {
            try
            {
                HotReloadUnityMessageBinding binding =
                    HotReloadUnityMessageForwarderFactory.CreateBinding(targetType, shims);
                _attacher.Bind(targetType, binding);
                failure = null;
                return true;
            }
            catch (Exception exception)
            {
                failure = exception.Message;
                return false;
            }
        }

        private static MethodInfo[] Lookup(Dictionary<Type, MethodInfo[]> source, Type targetType)
        {
            return source.TryGetValue(targetType, out MethodInfo[] shims) ? shims : null;
        }

        private static bool SameReferences(MethodInfo[] left, MethodInfo[] right)
        {
            if (left == null || right == null || left.Length != right.Length)
            {
                return false;
            }

            for (int index = 0; index < left.Length; index++)
            {
                if (!ReferenceEquals(left[index], right[index]))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
