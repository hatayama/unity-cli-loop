using System;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Domain;

using CodeTextMask = io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationParsingRules.CodeTextMask;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies how timing migration recognizes method declarations and their external contracts.
    /// </summary>
    public sealed class ThirdPartyToolMigrationTimingMethodDeclarationRulesTests
    {
        /// <summary>
        /// Verifies an empty name read before a parameter list is not treated as a method declaration.
        /// </summary>
        [Test]
        public void IsMethodDeclarationParameterListName_WhenNameIsEmpty_ReturnsFalse()
        {
            bool result = ThirdPartyToolMigrationTimingMethodDeclarationRules.IsMethodDeclarationParameterListName(
                string.Empty);

            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Verifies a parameter list outside any type declaration is never treated as a constructor.
        /// </summary>
        [Test]
        public void IsConstructorDeclarationParameterList_WhenNoContainingType_ReturnsFalse()
        {
            string source = "void Run(int value) { }\n";
            CodeTextMask codeTextMask = CodeTextMask.CreateUncached(source);

            bool result = ThirdPartyToolMigrationTimingMethodDeclarationRules.IsConstructorDeclarationParameterList(
                source,
                codeTextMask,
                source.IndexOf('('),
                string.Empty);

            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Verifies an explicit interface implementation keeps its signature because it is bound to an interface contract.
        /// </summary>
        [Test]
        public void IsContractBoundMethodDeclaration_WhenExplicitInterfaceImplementation_ReturnsTrue()
        {
            string source = "class Runner\n{\n    void IRunner.Run(int value) { }\n}\n";
            CodeTextMask codeTextMask = CodeTextMask.CreateUncached(source);

            bool result = ThirdPartyToolMigrationTimingMethodDeclarationRules.IsContractBoundMethodDeclaration(
                source,
                codeTextMask,
                "Run",
                source.IndexOf('('),
                "int value");

            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Verifies whitespace between a method name and its parameter list is skipped when locating the name.
        /// </summary>
        [Test]
        public void ReadMethodNameStartIndexBeforeParameterList_WhenWhitespaceBeforeParenthesis_ReturnsNameStart()
        {
            string source = "void Run (int value)";

            int result = ThirdPartyToolMigrationTimingMethodDeclarationRules.ReadMethodNameStartIndexBeforeParameterList(
                source,
                source.IndexOf('('));

            Assert.That(result, Is.EqualTo(source.IndexOf("Run", StringComparison.Ordinal)));
        }

        /// <summary>
        /// Verifies a non-public method inside a type with a base list is not treated as a possible external contract.
        /// </summary>
        [Test]
        public void IsPossibleExternalContractMethodDeclaration_WhenMethodIsNotPublic_ReturnsFalse()
        {
            string source = "class Runner : RunnerBase\n{\n    void Run(int value) { }\n}\n";
            CodeTextMask codeTextMask = CodeTextMask.CreateUncached(source);

            bool result = ThirdPartyToolMigrationTimingMethodDeclarationRules.IsPossibleExternalContractMethodDeclaration(
                source,
                codeTextMask,
                source.IndexOf("Run(", StringComparison.Ordinal));

            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Verifies a base list on an earlier body-less positional record does not count for a member of a later type.
        /// </summary>
        [Test]
        public void IsInsideTypeWithBaseList_WhenOnlyPrecedingBodylessRecordHasBaseList_ReturnsFalse()
        {
            string source =
                "record Point(int X) : PointBase;\n" +
                "class Runner\n{\n    public void Run() { }\n}\n";
            CodeTextMask codeTextMask = CodeTextMask.CreateUncached(source);

            bool result = ThirdPartyToolMigrationTimingMethodDeclarationRules.IsInsideTypeWithBaseList(
                source,
                codeTextMask,
                source.IndexOf("Run(", StringComparison.Ordinal));

            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Verifies an interface method with the same name and parameter count is reported as a contract.
        /// </summary>
        [Test]
        public void ContainsInterfaceMethodContract_WhenInterfaceDeclaresSameNameAndCount_ReturnsTrue()
        {
            string source =
                "interface IRunner\n{\n    void Run(int value, PlayerLoopTiming timing);\n}\n" +
                "class Runner\n{\n    public void Run(int value, PlayerLoopTiming timing) { }\n}\n";
            CodeTextMask codeTextMask = CodeTextMask.CreateUncached(source);

            bool result = ThirdPartyToolMigrationTimingMethodDeclarationRules.ContainsInterfaceMethodContract(
                source,
                codeTextMask,
                "Run",
                "int value, PlayerLoopTiming timing");

            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Verifies an interface method with the same name but a different parameter count is not a contract.
        /// </summary>
        [Test]
        public void ContainsInterfaceMethodContract_WhenInterfaceParameterCountDiffers_ReturnsFalse()
        {
            string source =
                "interface IRunner\n{\n    void Run(int value);\n}\n" +
                "class Runner\n{\n    public void Run(int value, PlayerLoopTiming timing) { }\n}\n";
            CodeTextMask codeTextMask = CodeTextMask.CreateUncached(source);

            bool result = ThirdPartyToolMigrationTimingMethodDeclarationRules.ContainsInterfaceMethodContract(
                source,
                codeTextMask,
                "Run",
                "int value, PlayerLoopTiming timing");

            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Verifies an interface declaration that only appears inside a comment is ignored.
        /// </summary>
        [Test]
        public void ContainsInterfaceMethodContract_WhenInterfaceIsInsideComment_ReturnsFalse()
        {
            string source =
                "// interface IRunner\n" +
                "class Runner\n{\n    public void Run(int value) { }\n}\n";
            CodeTextMask codeTextMask = CodeTextMask.CreateUncached(source);

            bool result = ThirdPartyToolMigrationTimingMethodDeclarationRules.ContainsInterfaceMethodContract(
                source,
                codeTextMask,
                "Run",
                "int value");

            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Verifies an interface header without a body at the end of the source is skipped.
        /// </summary>
        [Test]
        public void ContainsInterfaceMethodContract_WhenInterfaceHasNoBody_ReturnsFalse()
        {
            string source = "interface IRunner";
            CodeTextMask codeTextMask = CodeTextMask.CreateUncached(source);

            bool result = ThirdPartyToolMigrationTimingMethodDeclarationRules.ContainsInterfaceMethodContract(
                source,
                codeTextMask,
                "Run",
                "int value");

            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Verifies an interface body without a closing brace is skipped.
        /// </summary>
        [Test]
        public void ContainsInterfaceMethodContract_WhenInterfaceBodyIsUnterminated_ReturnsFalse()
        {
            string source = "interface IRunner\n{\n    void Run(int value);\n";
            CodeTextMask codeTextMask = CodeTextMask.CreateUncached(source);

            bool result = ThirdPartyToolMigrationTimingMethodDeclarationRules.ContainsInterfaceMethodContract(
                source,
                codeTextMask,
                "Run",
                "int value");

            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Verifies a method name that appears inside a comment in an interface body is not a contract.
        /// </summary>
        [Test]
        public void ContainsInterfaceMethodContract_WhenMethodNameIsInsideCommentInInterface_ReturnsFalse()
        {
            string source = "interface IRunner\n{\n    void Stop(/* Run( */ int value);\n}\n";
            CodeTextMask codeTextMask = CodeTextMask.CreateUncached(source);

            bool result = ThirdPartyToolMigrationTimingMethodDeclarationRules.ContainsInterfaceMethodContract(
                source,
                codeTextMask,
                "Run",
                "int value");

            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Verifies an interface method whose parameter list never closes is not a contract.
        /// </summary>
        [Test]
        public void ContainsInterfaceMethodContract_WhenInterfaceParameterListIsUnterminated_ReturnsFalse()
        {
            string source = "interface IRunner\n{\n    void Run(int value\n}\n";
            CodeTextMask codeTextMask = CodeTextMask.CreateUncached(source);

            bool result = ThirdPartyToolMigrationTimingMethodDeclarationRules.ContainsInterfaceMethodContract(
                source,
                codeTextMask,
                "Run",
                "int value");

            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Verifies a parenthesis after an unmatched closing angle bracket yields no method name.
        /// </summary>
        [Test]
        public void ReadMethodNameBeforeParameterList_WhenAngleBracketHasNoOpening_ReturnsEmpty()
        {
            string source = "if (count > (limit))";

            string result = ThirdPartyToolMigrationTimingMethodDeclarationRules.ReadMethodNameBeforeParameterList(
                source,
                source.IndexOf("(limit", StringComparison.Ordinal));

            Assert.That(result, Is.Empty);
        }

        /// <summary>
        /// Verifies whitespace between a generic method name and its type arguments is skipped when reading the name.
        /// </summary>
        [Test]
        public void ReadMethodNameBeforeParameterList_WhenWhitespaceBeforeTypeArguments_ReturnsMethodName()
        {
            string source = "void Run <T>(T value)";

            string result = ThirdPartyToolMigrationTimingMethodDeclarationRules.ReadMethodNameBeforeParameterList(
                source,
                source.IndexOf('('));

            Assert.That(result, Is.EqualTo("Run"));
        }
    }
}
