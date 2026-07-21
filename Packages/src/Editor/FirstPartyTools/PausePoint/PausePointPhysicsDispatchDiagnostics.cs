namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Pure part of the physics-callback dispatch diagnostics snapshot: whether an instance count
    /// is meaningful for the declaring type at all.
    /// </summary>
    internal static class PausePointPhysicsDispatchDiagnostics
    {
        // -1 signals "not applicable": counting instances only means something when the declaring
        // type is a MonoBehaviour (the physics dispatch miss this diagnostic exists for is scoped
        // to MonoBehaviour physics message methods).
        public static int ResolveInstanceCount(bool isMonoBehaviourDerived, int monoBehaviourInstanceCount)
        {
            return isMonoBehaviourDerived ? monoBehaviourInstanceCount : -1;
        }
    }
}
