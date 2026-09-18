using System;
using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// The Unity message names hot reload recognizes on an added method, split into the ones a
    /// proxy component forwards to a live instance and the ones that need a compile.
    /// </summary>
    internal static class HotReloadUnityMessageNames
    {
        // Why two sets instead of one set plus a flag: the caller asks two independent questions.
        // "Is this a Unity message at all" decides whether the added method keeps its ordinary
        // note, and "can it be forwarded" decides whether a proxy is built for it.
        private static readonly HashSet<string> ForwardedNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "Start",
            "Update",
            "LateUpdate",
            "FixedUpdate",
            "OnGUI",
            "OnCollisionEnter",
            "OnCollisionExit",
            "OnCollisionStay",
            "OnTriggerEnter",
            "OnTriggerExit",
            "OnTriggerStay",
            "OnCollisionEnter2D",
            "OnCollisionExit2D",
            "OnCollisionStay2D",
            "OnTriggerEnter2D",
            "OnTriggerExit2D",
            "OnTriggerStay2D",
            "OnMouseDown",
            "OnMouseUp",
            "OnMouseEnter",
            "OnMouseExit",
            "OnMouseOver",
            "OnMouseDrag",
            "OnBecameVisible",
            "OnBecameInvisible",
            "OnApplicationQuit",
            "OnApplicationPause",
            "OnApplicationFocus",
            "OnTransformChildrenChanged",
            "OnTransformParentChanged",
            "OnRectTransformDimensionsChange",
            "OnParticleCollision",
            "OnParticleTrigger",
            "OnControllerColliderHit",
            "OnJointBreak",
            "OnJointBreak2D",
            "OnAnimatorMove",
            "OnAnimatorIK"
        };

        // Why these stay out: the first four are lifecycle messages whose proxy timing does not
        // match the target's own (a proxy is attached and destroyed on its own schedule, so its
        // Awake/OnEnable/OnDisable/OnDestroy would fire at moments the target never sees), and
        // the last four are editor-only messages that run outside Play Mode, where no proxy exists.
        private static readonly HashSet<string> NotForwardedNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "Awake",
            "OnEnable",
            "OnDisable",
            "OnDestroy",
            "Reset",
            "OnValidate",
            "OnDrawGizmos",
            "OnDrawGizmosSelected"
        };

        /// <summary>Whether a proxy component forwards this message to the live target.</summary>
        internal static bool IsForwarded(string methodName)
        {
            return methodName != null && ForwardedNames.Contains(methodName);
        }

        /// <summary>Whether Unity's message dispatch knows this name at all.</summary>
        internal static bool IsKnownMessage(string methodName)
        {
            return IsForwarded(methodName) || (methodName != null && NotForwardedNames.Contains(methodName));
        }
    }
}
