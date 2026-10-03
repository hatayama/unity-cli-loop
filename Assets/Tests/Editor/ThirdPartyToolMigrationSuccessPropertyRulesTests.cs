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

        /// <summary>
        /// Verifies an attribute that shares its line with other code is removed together with the Success property it annotates.
        /// </summary>
        [Test]
        public void RemoveSuccessPropertyHidingDeclarationsInCode_WhenAttributeSharesLineWithCode_RemovesAttribute()
        {
            const string source =
                "class MyResponse : UnityCliLoopToolResponse\n{\n    int count; [SerializeField]\n    public bool Success { get; set; }\n}\n";

            (string content, int replacementCount) =
                ThirdPartyToolMigrationSuccessPropertyRules.RemoveSuccessPropertyHidingDeclarationsInCode(source);

            Assert.That(content, Is.EqualTo("class MyResponse : UnityCliLoopToolResponse\n{\n    int count;\n}\n"));
            Assert.That(replacementCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies the code before a removed inline attribute stays on its own line when another member follows the Success property.
        /// </summary>
        [Test]
        public void RemoveSuccessPropertyHidingDeclarationsInCode_WhenAttributeSharesLineWithCodeAndMemberFollows_KeepsTheMemberOnItsLine()
        {
            const string source =
                "class MyResponse : UnityCliLoopToolResponse\n{\n    int count; [SerializeField]\n    public bool Success { get; set; }\n    public int Value { get; set; }\n}\n";

            (string content, int replacementCount) =
                ThirdPartyToolMigrationSuccessPropertyRules.RemoveSuccessPropertyHidingDeclarationsInCode(source);

            Assert.That(
                content,
                Is.EqualTo("class MyResponse : UnityCliLoopToolResponse\n{\n    int count;\n    public int Value { get; set; }\n}\n"));
            Assert.That(replacementCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies every attribute on a line holding several attributes is removed together with the Success property.
        /// </summary>
        [Test]
        public void RemoveSuccessPropertyHidingDeclarationsInCode_WhenSeveralAttributesShareALine_RemovesThemAll()
        {
            const string source =
                "class MyResponse : UnityCliLoopToolResponse\n{\n    [SerializeField][JsonProperty(\"success\")]\n    public bool Success { get; set; }\n}\n";

            (string content, int replacementCount) =
                ThirdPartyToolMigrationSuccessPropertyRules.RemoveSuccessPropertyHidingDeclarationsInCode(source);

            Assert.That(content, Is.EqualTo("class MyResponse : UnityCliLoopToolResponse\n{\n}\n"));
            Assert.That(replacementCount, Is.EqualTo(1));
        }
    }
}
