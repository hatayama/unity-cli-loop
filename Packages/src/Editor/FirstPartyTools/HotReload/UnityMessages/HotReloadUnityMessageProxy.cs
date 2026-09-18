using System;

using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// The base of every generated proxy component: it holds the target whose added Unity messages
    /// it stands in for, and turns a message Unity delivered to the proxy into a call on that target.
    /// </summary>
    /// <remarks>
    /// Why this type and its forwarding entry point are public: the generated proxy type lives in a
    /// dynamic assembly that no InternalsVisibleTo covers, and the IL it carries is emitted through
    /// a TypeBuilder, which the runtime checks for accessibility like any compiled call. Keeping
    /// everything the emitted IL touches public is what lets that IL stay free of the target type
    /// and the shim, both of which may be internal.
    /// </remarks>
    public abstract class HotReloadUnityMessageProxy : MonoBehaviour
    {
        // Why a thread-static slot instead of a property set after AddComponent: the managed
        // constructor runs inside AddComponent, and Unity may deliver messages to the component
        // before the AddComponent call returns. A field assigned in the constructor is the only
        // way the target is already there by then, and having no setter makes an implementation
        // that forgets to hand the target over fail visibly instead of forwarding to nothing.
        [ThreadStatic]
        private static MonoBehaviour s_pendingTarget;

        private readonly MonoBehaviour _target;

        protected HotReloadUnityMessageProxy()
        {
            _target = s_pendingTarget;
            s_pendingTarget = null;
        }

        /// <summary>The component whose added Unity messages this proxy forwards.</summary>
        public MonoBehaviour Target => _target;

        /// <summary>Whether this proxy has been retired and must forward nothing more.</summary>
        public bool IsUnbound { get; private set; }

        /// <summary>
        /// Hands the target to the next proxy constructed on this thread. The caller adds the
        /// component inside the returned scope, which clears the slot even when adding fails.
        /// </summary>
        public static IDisposable BeginPendingTarget(MonoBehaviour target)
        {
            Debug.Assert(target != null, "target must not be null.");
            s_pendingTarget = target;
            return new PendingTargetScope();
        }

        /// <summary>Stops this proxy forwarding, ahead of the destruction that follows it.</summary>
        public void Unbind()
        {
            IsUnbound = true;
        }

        /// <summary>
        /// Called by the emitted proxy method for the message in <paramref name="slot"/>. Instance
        /// method because the emitted IL passes itself as the receiver and reads no field of its own.
        /// </summary>
        public void Forward(HotReloadUnityMessageBinding binding, int slot, object[] args)
        {
            if (IsUnbound)
            {
                return;
            }

            // Unity's own == reports a destroyed component as null, which is how a target that was
            // removed on its own - with the proxy's GameObject still alive - is caught here.
            if (_target == null)
            {
                return;
            }

            if (binding.IsGated(slot) && !_target.isActiveAndEnabled)
            {
                return;
            }

            binding.Invoke(slot, _target, args);
        }

        private sealed class PendingTargetScope : IDisposable
        {
            public void Dispose()
            {
                s_pendingTarget = null;
            }
        }
    }
}
