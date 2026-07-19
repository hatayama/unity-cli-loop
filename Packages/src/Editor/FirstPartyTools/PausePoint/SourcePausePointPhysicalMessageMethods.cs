using System;
using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Names Unity's physics message methods, whose native dispatch caches its call path
    /// independently of a later Harmony patch (see PhysicalCallbackMayMissExistingInstanceWarning).
    /// </summary>
    internal static class SourcePausePointPhysicalMessageMethods
    {
        private static readonly HashSet<string> Names = new(StringComparer.Ordinal)
        {
            "OnCollisionEnter",
            "OnCollisionStay",
            "OnCollisionExit",
            "OnCollisionEnter2D",
            "OnCollisionStay2D",
            "OnCollisionExit2D",
            "OnTriggerEnter",
            "OnTriggerStay",
            "OnTriggerExit",
            "OnTriggerEnter2D",
            "OnTriggerStay2D",
            "OnTriggerExit2D",
            "OnParticleCollision",
        };

        public static bool IsPhysicalMessageMethod(string methodName)
        {
            return !string.IsNullOrEmpty(methodName) && Names.Contains(methodName);
        }
    }
}
