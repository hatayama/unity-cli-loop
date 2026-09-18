using System;
using System.Collections.Generic;

using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Keeps the live scene in step with the proxy types built for the added Unity messages: it
    /// attaches one proxy per target instance, and owns every proxy it attached until it takes it
    /// back off again.
    /// </summary>
    /// <remarks>
    /// Why ownership rather than a search of the scene: a proxy whose target was destroyed on its
    /// own has nothing left to find it by, and a proxy left behind by a domain reload would come
    /// back as a missing script. Holding the instances is what makes "take them all off" possible
    /// at any moment, including one where the targets are already gone.
    /// </remarks>
    internal sealed class HotReloadUnityMessageProxyAttacher
    {
        private readonly IHotReloadPlayModeQuery _playMode;
        private readonly HotReloadUnityMessageProxyTypeBuilder _builder;
        private readonly Dictionary<Type, BoundProxyType> _boundByTarget =
            new Dictionary<Type, BoundProxyType>();
        private readonly List<OwnedProxy> _owned = new List<OwnedProxy>();
        private bool _suspended;

        internal HotReloadUnityMessageProxyAttacher(
            IHotReloadPlayModeQuery playMode,
            HotReloadUnityMessageProxyTypeBuilder builder)
        {
            Debug.Assert(playMode != null, "playMode must not be null.");
            Debug.Assert(builder != null, "builder must not be null.");
            _playMode = playMode;
            _builder = builder;
        }

        /// <summary>The target types a proxy is currently attached for.</summary>
        internal IReadOnlyList<Type> BoundTargetTypes => new List<Type>(_boundByTarget.Keys);

        /// <summary>
        /// The proxy type currently built for <paramref name="targetType"/>, or null when the type
        /// is not bound. A caller tells a rebuild apart by the type changing identity.
        /// </summary>
        internal Type FindProxyType(Type targetType)
        {
            Debug.Assert(targetType != null, "targetType must not be null.");
            return _boundByTarget.TryGetValue(targetType, out BoundProxyType bound)
                ? bound.ProxyType
                : null;
        }

        /// <summary>
        /// Starts attaching proxies for <paramref name="targetType"/> under a newly built proxy
        /// type. A type bound already gets the new type, and the proxies of the previous one go at
        /// the next tick, so an added Start runs again on every instance.
        /// </summary>
        internal void Bind(Type targetType, HotReloadUnityMessageBinding binding)
        {
            Debug.Assert(targetType != null, "targetType must not be null.");
            Debug.Assert(binding != null, "binding must not be null.");
            _boundByTarget[targetType] = new BoundProxyType(_builder.Build(binding), binding);
        }

        /// <summary>
        /// Points the proxy type already built for <paramref name="targetType"/> at
        /// <paramref name="binding"/> when it declares the same messages, and answers false, having
        /// changed nothing, when the type is not bound or its messages differ.
        /// </summary>
        /// <remarks>
        /// Why keep the type: the tick replaces a proxy only when its type changed, and replacing it
        /// is an AddComponent that runs an added Start on every live instance again. A reload that
        /// only produced new shims for the same messages must not reset the game that way.
        /// </remarks>
        internal bool TryRebind(Type targetType, HotReloadUnityMessageBinding binding)
        {
            Debug.Assert(targetType != null, "targetType must not be null.");
            Debug.Assert(binding != null, "binding must not be null.");
            if (!_boundByTarget.TryGetValue(targetType, out BoundProxyType bound)
                || !bound.Binding.HasSameShape(binding))
            {
                return false;
            }

            _builder.Rebind(bound.ProxyType, binding);
            _boundByTarget[targetType] = new BoundProxyType(bound.ProxyType, binding);
            return true;
        }

        /// <summary>
        /// Stops attaching proxies for <paramref name="targetType"/> and takes off the ones that
        /// are attached.
        /// </summary>
        internal void Unbind(Type targetType)
        {
            Debug.Assert(targetType != null, "targetType must not be null.");
            _boundByTarget.Remove(targetType);
            for (int index = _owned.Count - 1; index >= 0; index--)
            {
                if (_owned[index].TargetType != targetType)
                {
                    continue;
                }

                Retire(_owned[index]);
                _owned.RemoveAt(index);
            }
        }

        /// <summary>Takes every proxy off and forgets every binding.</summary>
        internal void Clear()
        {
            foreach (OwnedProxy owned in _owned)
            {
                Retire(owned);
            }

            _owned.Clear();
            _boundByTarget.Clear();
        }

        /// <summary>Stops attaching until <see cref="Resume"/>, without forgetting the bindings.</summary>
        internal void Suspend()
        {
            _suspended = true;
        }

        internal void Resume()
        {
            _suspended = false;
        }

        /// <summary>
        /// Brings the scene up to date: retires the proxies that no longer match, and attaches one
        /// to every target instance that has none.
        /// </summary>
        internal void Tick()
        {
            // Why both: Play Mode is where Unity delivers the messages at all, and the editor keeps
            // ticking between the request to leave Play Mode and the moment it actually leaves, so
            // a run that is on its way out must not attach proxies the stop would strand.
            if (_suspended || !_playMode.IsPlaying)
            {
                return;
            }

            RetireStaleProxies();
            AttachMissingProxies();
        }

        private void RetireStaleProxies()
        {
            for (int index = _owned.Count - 1; index >= 0; index--)
            {
                OwnedProxy owned = _owned[index];
                if (IsCurrent(owned))
                {
                    continue;
                }

                Retire(owned);
                _owned.RemoveAt(index);
            }
        }

        private bool IsCurrent(OwnedProxy owned)
        {
            // Unity's own == answers true for a proxy or a target that was destroyed, which is how
            // a target removed on its own, and a proxy removed with its GameObject, are caught.
            if (owned.Proxy == null || owned.Proxy.Target == null)
            {
                return false;
            }

            return _boundByTarget.TryGetValue(owned.TargetType, out BoundProxyType bound)
                && bound.ProxyType == owned.ProxyType;
        }

        private void AttachMissingProxies()
        {
            foreach (KeyValuePair<Type, BoundProxyType> pair in _boundByTarget)
            {
                // Inactive instances are included: a GameObject switched back on during Play Mode
                // starts receiving messages without passing through this sweep again.
#if UNITY_6000_4_OR_NEWER
                UnityEngine.Object[] instances = UnityEngine.Object.FindObjectsByType(
                    pair.Key,
                    FindObjectsInactive.Include);
#else
                UnityEngine.Object[] instances = UnityEngine.Object.FindObjectsByType(
                    pair.Key,
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None);
#endif
                foreach (UnityEngine.Object instance in instances)
                {
                    AttachIfMissing((MonoBehaviour)instance, pair.Key, pair.Value.ProxyType);
                }
            }
        }

        private void AttachIfMissing(MonoBehaviour target, Type targetType, Type proxyType)
        {
            if (HasProxy(target, proxyType))
            {
                return;
            }

            HotReloadUnityMessageProxy proxy;
            // The scope hands the target to the constructor that runs inside AddComponent, and
            // clears the slot even when adding throws, so the next proxy cannot inherit it.
            using (HotReloadUnityMessageProxy.BeginPendingTarget(target))
            {
                proxy = (HotReloadUnityMessageProxy)target.gameObject.AddComponent(proxyType);
            }

            if (proxy == null)
            {
                return;
            }

            // Why hidden and not saved: the proxy belongs to this editor session only, and a scene
            // that saved it would come back with a component whose type no longer exists.
            proxy.hideFlags = HideFlags.HideAndDontSave;
            _owned.Add(new OwnedProxy(proxy, targetType, proxyType));
        }

        private bool HasProxy(MonoBehaviour target, Type proxyType)
        {
            foreach (OwnedProxy owned in _owned)
            {
                if (owned.ProxyType == proxyType && ReferenceEquals(owned.Proxy.Target, target))
                {
                    return true;
                }
            }

            return false;
        }

        private static void Retire(OwnedProxy owned)
        {
            if (owned.Proxy == null)
            {
                return;
            }

            // Unbind before destroying: Unity may still deliver a message to a component that is
            // on its way out, and forwarding it would call a target this proxy no longer serves.
            owned.Proxy.Unbind();
            // Why DestroyImmediate: a tick runs on the editor's update, outside Unity's message
            // dispatch, and the deferred destroy of Object.Destroy never runs outside Play Mode.
            UnityEngine.Object.DestroyImmediate(owned.Proxy);
        }

        /// <summary>The proxy type a target type is bound to, and the binding it forwards through.</summary>
        private readonly struct BoundProxyType
        {
            internal BoundProxyType(Type proxyType, HotReloadUnityMessageBinding binding)
            {
                Debug.Assert(proxyType != null, "proxyType must not be null.");
                Debug.Assert(binding != null, "binding must not be null.");
                ProxyType = proxyType;
                Binding = binding;
            }

            internal Type ProxyType { get; }

            internal HotReloadUnityMessageBinding Binding { get; }
        }

        private sealed class OwnedProxy
        {
            internal OwnedProxy(HotReloadUnityMessageProxy proxy, Type targetType, Type proxyType)
            {
                Proxy = proxy;
                TargetType = targetType;
                ProxyType = proxyType;
            }

            internal HotReloadUnityMessageProxy Proxy { get; }

            internal Type TargetType { get; }

            internal Type ProxyType { get; }
        }
    }
}
