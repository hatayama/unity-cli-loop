using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies detection of Success declarations hiding UnityCliLoopToolResponse.Success.
    /// </summary>
    public sealed class ThirdPartyToolMigrationSuccessPropertyRulesTests
    {
        /// <summary>
        /// Verifies that a commented-out Success auto-property in a response class is not reported as hiding the base member.
        /// </summary>
        [Test]
        public void ContainsSuccessPropertyHidingUnityCliLoopToolResponse_WhenAutoPropertyIsOnlyInComment_ReturnsFalse()
        {
            const string source =
                "class MyResponse : UnityCliLoopToolResponse {\n// public bool Success { get; set; }\n}";

            bool contains =
                ThirdPartyToolMigrationSuccessPropertyRules.ContainsSuccessPropertyHidingUnityCliLoopToolResponse(source);

            Assert.That(contains, Is.False);
        }

        /// <summary>
        /// Verifies that a commented-out expression-bodied Success property in a response class is not reported as non-auto hiding.
        /// </summary>
        [Test]
        public void ContainsNonAutoPropertySuccessHidingUnityCliLoopToolResponse_WhenPropertyIsOnlyInComment_ReturnsFalse()
        {
            const string source =
                "class MyResponse : UnityCliLoopToolResponse {\n// public bool Success => true;\n}";

            bool contains =
                ThirdPartyToolMigrationSuccessPropertyRules.ContainsNonAutoPropertySuccessHidingUnityCliLoopToolResponse(source);

            Assert.That(contains, Is.False);
        }
    }
}
