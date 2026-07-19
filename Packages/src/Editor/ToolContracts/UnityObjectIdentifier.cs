using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.ToolContracts
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
            Debug.Assert(unityObject is not null, "unityObject must not be null.");
#pragma warning disable 618, 619 // The public and Go contracts remain int, and Unity provides no supported EntityId-to-int conversion.
            return unityObject.GetInstanceID();
#pragma warning restore 618, 619
        }
    }
}
