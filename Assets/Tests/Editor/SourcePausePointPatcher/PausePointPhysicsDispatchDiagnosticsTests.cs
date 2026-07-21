using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies the pure instance-count resolution rule used by physics-callback dispatch
    /// diagnostics: counting only applies when the declaring type is a MonoBehaviour.
    /// </summary>
    [TestFixture]
    public sealed class PausePointPhysicsDispatchDiagnosticsTests
    {
        // What: a MonoBehaviour-derived declaring type reports the actual scene instance count.
        [Test]
        public void ResolveInstanceCount_WithMonoBehaviourDerivedType_ReturnsActualCount()
        {
            int result = PausePointPhysicsDispatchDiagnostics.ResolveInstanceCount(
                isMonoBehaviourDerived: true, monoBehaviourInstanceCount: 3);

            Assert.AreEqual(3, result);
        }

        // What: a non-MonoBehaviour declaring type reports -1, since instance counting has no
        // meaning for it (the diagnostics exist for MonoBehaviour physics message dispatch only).
        [Test]
        public void ResolveInstanceCount_WithNonMonoBehaviourType_ReturnsNegativeOne()
        {
            int result = PausePointPhysicsDispatchDiagnostics.ResolveInstanceCount(
                isMonoBehaviourDerived: false, monoBehaviourInstanceCount: 0);

            Assert.AreEqual(-1, result);
        }
    }
}
