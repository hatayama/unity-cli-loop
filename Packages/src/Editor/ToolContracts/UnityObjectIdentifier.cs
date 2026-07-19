using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Provides the integer object handle required by the current pause-point response contract.
    /// </summary>
    internal static class UnityObjectIdentifier
    {
        /// <summary>
        /// Gets an object handle while keeping the existing integer response contract compatible with older Unity versions.
        /// </summary>
        public static int GetInstanceId(Object unityObject)
        {
            Debug.Assert(unityObject != null, "unityObject must not be null.");
#if UNITY_6000_4_OR_NEWER
            return (int)unityObject.GetEntityId();
#else
            return unityObject.GetInstanceID();
#endif
        }
    }
}
