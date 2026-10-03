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

        /// <summary>
        /// Verifies removing a Success property without attributes keeps the indentation of the member that follows it.
        /// </summary>
        [Test]
        public void RemoveSuccessPropertyHidingDeclarationsInCode_WhenMemberFollows_KeepsItsIndentation()
        {
            const string source =
                "class MyResponse : UnityCliLoopToolResponse\n{\n    public bool Success { get; set; }\n    public int Value { get; set; }\n}\n";

            (string content, int replacementCount) =
                ThirdPartyToolMigrationSuccessPropertyRules.RemoveSuccessPropertyHidingDeclarationsInCode(source);

            Assert.That(
                content,
                Is.EqualTo("class MyResponse : UnityCliLoopToolResponse\n{\n    public int Value { get; set; }\n}\n"));
            Assert.That(replacementCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies an inline attribute is removed and the kept code keeps its CRLF line break when the source uses CRLF.
        /// </summary>
        [Test]
        public void RemoveSuccessPropertyHidingDeclarationsInCode_WhenAttributeSharesLineWithCodeInCrlfSource_KeepsTheCrlfLineBreak()
        {
            const string source =
                "class MyResponse : UnityCliLoopToolResponse\r\n{\r\n    int count; [SerializeField]\r\n    public bool Success { get; set; }\r\n    public int Value { get; set; }\r\n}\r\n";

            (string content, int replacementCount) =
                ThirdPartyToolMigrationSuccessPropertyRules.RemoveSuccessPropertyHidingDeclarationsInCode(source);

            Assert.That(
                content,
                Is.EqualTo("class MyResponse : UnityCliLoopToolResponse\r\n{\r\n    int count;\r\n    public int Value { get; set; }\r\n}\r\n"));
            Assert.That(replacementCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies a Success property's XML doc comment is removed with it even when the property has no attributes.
        /// </summary>
        [Test]
        public void RemoveSuccessPropertyHidingDeclarationsInCode_WhenPropertyHasOnlyADocComment_RemovesTheDocComment()
        {
            const string source =
                "class MyResponse : UnityCliLoopToolResponse\n{\n    /// <summary>Whether it worked.</summary>\n    public bool Success { get; set; }\n    public int Value { get; set; }\n}\n";

            (string content, int replacementCount) =
                ThirdPartyToolMigrationSuccessPropertyRules.RemoveSuccessPropertyHidingDeclarationsInCode(source);

            Assert.That(
                content,
                Is.EqualTo("class MyResponse : UnityCliLoopToolResponse\n{\n    public int Value { get; set; }\n}\n"));
            Assert.That(replacementCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies an ordinary comment that starts with four slashes is kept when the property above it is removed.
        /// </summary>
        [Test]
        public void RemoveSuccessPropertyHidingDeclarationsInCode_WhenPrecededByAFourSlashComment_KeepsTheComment()
        {
            const string source =
                "class MyResponse : UnityCliLoopToolResponse\n{\n    //// old note\n    public bool Success { get; set; }\n}\n";

            (string content, int replacementCount) =
                ThirdPartyToolMigrationSuccessPropertyRules.RemoveSuccessPropertyHidingDeclarationsInCode(source);

            Assert.That(content, Is.EqualTo("class MyResponse : UnityCliLoopToolResponse\n{\n    //// old note\n}\n"));
            Assert.That(replacementCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies a comment after the property on the same line keeps its indentation when the property is removed.
        /// </summary>
        [Test]
        public void RemoveSuccessPropertyHidingDeclarationsInCode_WhenACommentFollowsOnTheSameLine_KeepsItsIndentation()
        {
            const string source =
                "class MyResponse : UnityCliLoopToolResponse\n{\n    public bool Success { get; set; } // required\n    public int Value { get; set; }\n}\n";

            (string content, int replacementCount) =
                ThirdPartyToolMigrationSuccessPropertyRules.RemoveSuccessPropertyHidingDeclarationsInCode(source);

            Assert.That(
                content,
                Is.EqualTo("class MyResponse : UnityCliLoopToolResponse\n{\n    // required\n    public int Value { get; set; }\n}\n"));
            Assert.That(replacementCount, Is.EqualTo(1));
        }
    }
}
