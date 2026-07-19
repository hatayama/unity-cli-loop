using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies which method names are treated as Unity physics message methods for the
    /// physical-callback pause-point warning.
    /// </summary>
    [TestFixture]
    public sealed class SourcePausePointPhysicalMessageMethodsTests
    {
        [TestCase("OnCollisionEnter")]
        [TestCase("OnCollisionStay")]
        [TestCase("OnCollisionExit")]
        [TestCase("OnCollisionEnter2D")]
        [TestCase("OnCollisionStay2D")]
        [TestCase("OnCollisionExit2D")]
        [TestCase("OnTriggerEnter")]
        [TestCase("OnTriggerStay")]
        [TestCase("OnTriggerExit")]
        [TestCase("OnTriggerEnter2D")]
        [TestCase("OnTriggerStay2D")]
        [TestCase("OnTriggerExit2D")]
        [TestCase("OnParticleCollision")]
        public void IsPhysicalMessageMethod_WhenNameIsAPhysicsMessageMethod_ReturnsTrue(string methodName)
        {
            // Verifies every known Unity physics message method name is recognized.
            bool result = SourcePausePointPhysicalMessageMethods.IsPhysicalMessageMethod(methodName);

            Assert.That(result, Is.True);
        }

        [TestCase("Update")]
        [TestCase("LateUpdate")]
        [TestCase("FixedUpdate")]
        [TestCase("Awake")]
        [TestCase("OnEnable")]
        [TestCase("Add")]
        [TestCase("")]
        [TestCase(null)]
        public void IsPhysicalMessageMethod_WhenNameIsNotAPhysicsMessageMethod_ReturnsFalse(string methodName)
        {
            // Verifies ordinary lifecycle methods and non-message names do not match, so the
            // warning never fires for methods like Update() that patch and hit reliably.
            bool result = SourcePausePointPhysicalMessageMethods.IsPhysicalMessageMethod(methodName);

            Assert.That(result, Is.False);
        }
    }
}
