using System;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Domain;

using CodeTextMask = io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationParsingRules.CodeTextMask;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies how timing migration locates invocations and decides whether a caller targets a removed signature.
    /// </summary>
    public sealed class ThirdPartyToolMigrationTimingInvocationRulesTests
    {
        /// <summary>
        /// Verifies an unterminated generic argument list yields no invocation parenthesis.
        /// </summary>
        [Test]
        public void FindInvocationOpenParenthesisIndex_WhenGenericArgumentListIsUnterminated_ReturnsMinusOne()
        {
            string source = "(Run<int";
            CodeTextMask codeTextMask = CodeTextMask.CreateUncached(source);

            int result = ThirdPartyToolMigrationTimingInvocationRules.FindInvocationOpenParenthesisIndex(
                source,
                codeTextMask,
                source.IndexOf('<'));

            Assert.That(result, Is.EqualTo(-1));
        }

        /// <summary>
        /// Verifies a method name that is not followed by a parenthesis yields no invocation parenthesis.
        /// </summary>
        [Test]
        public void FindInvocationOpenParenthesisIndex_WhenNoParenthesisFollows_ReturnsMinusOne()
        {
            string source = "Action action = Run;";
            CodeTextMask codeTextMask = CodeTextMask.CreateUncached(source);

            int result = ThirdPartyToolMigrationTimingInvocationRules.FindInvocationOpenParenthesisIndex(
                source,
                codeTextMask,
                source.IndexOf("Run;", StringComparison.Ordinal) + "Run".Length);

            Assert.That(result, Is.EqualTo(-1));
        }

        /// <summary>
        /// Verifies angle brackets inside a comment do not close a generic argument list.
        /// </summary>
        [Test]
        public void FindGenericArgumentListEndIndex_WhenCommentContainsClosingAngle_ReturnsCodeClosingAngle()
        {
            string source = "Run<int /* > */>(value)";
            CodeTextMask codeTextMask = CodeTextMask.CreateUncached(source);

            int result = ThirdPartyToolMigrationTimingInvocationRules.FindGenericArgumentListEndIndex(
                source,
                codeTextMask,
                source.IndexOf('<'));

            Assert.That(result, Is.EqualTo(source.IndexOf(">(", StringComparison.Ordinal)));
        }

        /// <summary>
        /// Verifies a generic argument list that never closes yields minus one.
        /// </summary>
        [Test]
        public void FindGenericArgumentListEndIndex_WhenUnterminated_ReturnsMinusOne()
        {
            string source = "Run<Dictionary<int, string>";
            CodeTextMask codeTextMask = CodeTextMask.CreateUncached(source);

            int result = ThirdPartyToolMigrationTimingInvocationRules.FindGenericArgumentListEndIndex(
                source,
                codeTextMask,
                source.IndexOf('<'));

            Assert.That(result, Is.EqualTo(-1));
        }

        /// <summary>
        /// Verifies a caller passing more arguments than the original signature accepted is not migrated.
        /// </summary>
        [Test]
        public void ShouldMigrateLegacyPlayerLoopTimingCaller_WhenCallerHasMoreArgumentsThanSignature_ReturnsFalse()
        {
            string source =
                "class Runner\n{\n    void Call() { Run(1, PlayerLoopTiming.Update, 3); }\n}\n";
            CodeTextMask codeTextMask = CodeTextMask.CreateUncached(source);

            bool result = ThirdPartyToolMigrationTimingInvocationRules.ShouldMigrateLegacyPlayerLoopTimingCaller(
                source,
                codeTextMask,
                source.IndexOf("Run(", StringComparison.Ordinal),
                new[] { "1", " PlayerLoopTiming.Update", " 3" },
                CreateValueAndTimingSignature("Runner"));

            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Verifies a removed signature without a declaring type never matches a caller.
        /// </summary>
        [Test]
        public void DoesPlayerLoopTimingCallerTargetRemovedSignature_WhenDeclaringTypeIsEmpty_ReturnsFalse()
        {
            string source = "Run(1, PlayerLoopTiming.Update);\n";
            CodeTextMask codeTextMask = CodeTextMask.CreateUncached(source);

            bool result = ThirdPartyToolMigrationTimingInvocationRules.DoesPlayerLoopTimingCallerTargetRemovedSignature(
                source,
                codeTextMask,
                0,
                CreateValueAndTimingSignature(string.Empty));

            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Verifies a this-qualified call targets the removed signature when it is made inside the declaring type.
        /// </summary>
        [Test]
        public void DoesPlayerLoopTimingCallerTargetRemovedSignature_WhenThisCallInsideDeclaringType_ReturnsTrue()
        {
            string source =
                "namespace Tools\n{\n    class Runner\n    {\n        void Call() { this.Run(1, PlayerLoopTiming.Update); }\n    }\n}\n";
            CodeTextMask codeTextMask = CodeTextMask.CreateUncached(source);

            bool result = ThirdPartyToolMigrationTimingInvocationRules.DoesPlayerLoopTimingCallerTargetRemovedSignature(
                source,
                codeTextMask,
                source.IndexOf("Run(", StringComparison.Ordinal),
                CreateValueAndTimingSignature("Tools.Runner"));

            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Verifies a this-qualified call made from a type other than the declaring type does not target the removed signature.
        /// </summary>
        [Test]
        public void DoesPlayerLoopTimingCallerTargetRemovedSignature_WhenThisCallInsideOtherType_ReturnsFalse()
        {
            string source =
                "class Caller\n{\n    void Call() { this.Run(1, PlayerLoopTiming.Update); }\n}\n";
            CodeTextMask codeTextMask = CodeTextMask.CreateUncached(source);

            bool result = ThirdPartyToolMigrationTimingInvocationRules.DoesPlayerLoopTimingCallerTargetRemovedSignature(
                source,
                codeTextMask,
                source.IndexOf("Run(", StringComparison.Ordinal),
                CreateValueAndTimingSignature("Runner"));

            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Verifies a base-qualified call inside the declaring type targets that type's base class, not the removed signature.
        /// </summary>
        [Test]
        public void DoesPlayerLoopTimingCallerTargetRemovedSignature_WhenBaseCallInsideDeclaringTypeWithOtherBase_ReturnsFalse()
        {
            string source =
                "class Runner : MonoBehaviour\n{\n    void Call() { base.Run(1, PlayerLoopTiming.Update); }\n}\n";
            CodeTextMask codeTextMask = CodeTextMask.CreateUncached(source);

            bool result = ThirdPartyToolMigrationTimingInvocationRules.DoesPlayerLoopTimingCallerTargetRemovedSignature(
                source,
                codeTextMask,
                source.LastIndexOf("Run(", StringComparison.Ordinal),
                CreateValueAndTimingSignature("Runner"));

            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Verifies a base-qualified call inside a type without a base list does not target the removed signature of that type.
        /// </summary>
        [Test]
        public void DoesPlayerLoopTimingCallerTargetRemovedSignature_WhenBaseCallInsideTypeWithoutBaseList_ReturnsFalse()
        {
            string source =
                "class Runner\n{\n    void Call() { base.Run(1, PlayerLoopTiming.Update); }\n}\n";
            CodeTextMask codeTextMask = CodeTextMask.CreateUncached(source);

            bool result = ThirdPartyToolMigrationTimingInvocationRules.DoesPlayerLoopTimingCallerTargetRemovedSignature(
                source,
                codeTextMask,
                source.LastIndexOf("Run(", StringComparison.Ordinal),
                CreateValueAndTimingSignature("Runner"));

            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Verifies a base-qualified call inside a type derived from the declaring type targets the removed signature.
        /// </summary>
        [Test]
        public void DoesPlayerLoopTimingCallerTargetRemovedSignature_WhenBaseCallInsideDerivedType_ReturnsTrue()
        {
            string source =
                "class Derived : Runner\n{\n    void Call() { base.Run(1, PlayerLoopTiming.Update); }\n}\n";
            CodeTextMask codeTextMask = CodeTextMask.CreateUncached(source);

            bool result = ThirdPartyToolMigrationTimingInvocationRules.DoesPlayerLoopTimingCallerTargetRemovedSignature(
                source,
                codeTextMask,
                source.LastIndexOf("Run(", StringComparison.Ordinal),
                CreateValueAndTimingSignature("Runner"));

            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Verifies a base-qualified call targets the removed signature when the base class is a constructed generic of the declaring type.
        /// </summary>
        [Test]
        public void DoesPlayerLoopTimingCallerTargetRemovedSignature_WhenBaseCallInsideTypeDerivedFromGenericBase_ReturnsTrue()
        {
            string source =
                "class Derived : Runner<int>, IDisposable\n{\n    void Call() { base.Run(1, PlayerLoopTiming.Update); }\n}\n";
            CodeTextMask codeTextMask = CodeTextMask.CreateUncached(source);

            bool result = ThirdPartyToolMigrationTimingInvocationRules.DoesPlayerLoopTimingCallerTargetRemovedSignature(
                source,
                codeTextMask,
                source.LastIndexOf("Run(", StringComparison.Ordinal),
                CreateValueAndTimingSignature("Runner"));

            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Verifies a base type name is resolved against the enclosing namespace like other target expressions.
        /// </summary>
        [Test]
        public void DoesPlayerLoopTimingCallerTargetRemovedSignature_WhenBaseCallInsideDerivedTypeInNamespace_ReturnsTrue()
        {
            string source =
                "namespace Game\n{\n    class Derived : Runner\n    {\n        void Call() { base.Run(1, PlayerLoopTiming.Update); }\n    }\n}\n";
            CodeTextMask codeTextMask = CodeTextMask.CreateUncached(source);

            bool result = ThirdPartyToolMigrationTimingInvocationRules.DoesPlayerLoopTimingCallerTargetRemovedSignature(
                source,
                codeTextMask,
                source.LastIndexOf("Run(", StringComparison.Ordinal),
                CreateValueAndTimingSignature("Game.Runner"));

            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Verifies a base-qualified call does not target an interface's removed signature when the base list names only that interface.
        /// </summary>
        [Test]
        public void DoesPlayerLoopTimingCallerTargetRemovedSignature_WhenBaseListNamesOnlyInterface_ReturnsFalse()
        {
            string source =
                "interface IRunner\n{\n    void Run(int value, PlayerLoopTiming timing);\n}\n\n" +
                "class Derived : IRunner\n{\n    void Call() { base.Run(1, PlayerLoopTiming.Update); }\n}\n";
            CodeTextMask codeTextMask = CodeTextMask.CreateUncached(source);

            bool result = ThirdPartyToolMigrationTimingInvocationRules.DoesPlayerLoopTimingCallerTargetRemovedSignature(
                source,
                codeTextMask,
                source.LastIndexOf("Run(", StringComparison.Ordinal),
                CreateValueAndTimingSignature("IRunner"));

            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Verifies a colon inside a comment in the class header is not read as the start of the base list.
        /// </summary>
        [Test]
        public void DoesPlayerLoopTimingCallerTargetRemovedSignature_WhenHeaderCommentContainsColon_ReadsRealBaseList()
        {
            string source =
                "class Derived /* : Other */ : Runner\n{\n    void Call() { base.Run(1, PlayerLoopTiming.Update); }\n}\n";
            CodeTextMask codeTextMask = CodeTextMask.CreateUncached(source);

            bool result = ThirdPartyToolMigrationTimingInvocationRules.DoesPlayerLoopTimingCallerTargetRemovedSignature(
                source,
                codeTextMask,
                source.LastIndexOf("Run(", StringComparison.Ordinal),
                CreateValueAndTimingSignature("Runner"));

            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Verifies a non-token argument for a remaining CancellationToken parameter makes the caller incompatible.
        /// </summary>
        [Test]
        public void AreRemainingPlayerLoopTimingCallerArgumentsCompatible_WhenTokenParameterGetsNonTokenArgument_ReturnsFalse()
        {
            bool result =
                ThirdPartyToolMigrationTimingInvocationRules.AreRemainingPlayerLoopTimingCallerArgumentsCompatible(
                    new[] { "42" },
                    CreateTokenAndTimingSignature());

            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Verifies a token-like argument for a remaining CancellationToken parameter keeps the caller compatible.
        /// </summary>
        [Test]
        public void AreRemainingPlayerLoopTimingCallerArgumentsCompatible_WhenTokenParameterGetsTokenArgument_ReturnsTrue()
        {
            bool result =
                ThirdPartyToolMigrationTimingInvocationRules.AreRemainingPlayerLoopTimingCallerArgumentsCompatible(
                    new[] { "token" },
                    CreateTokenAndTimingSignature());

            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Verifies a named argument is matched to its parameter regardless of its position.
        /// </summary>
        [Test]
        public void ReadCallerArgumentForParameter_WhenArgumentIsNamed_ReturnsNamedValue()
        {
            string result = ThirdPartyToolMigrationTimingInvocationRules.ReadCallerArgumentForParameter(
                new[] { "timing: PlayerLoopTiming.Update", "value: 5" },
                new LegacyPlayerLoopTimingParameterDeclaration(0, "int", "value", false));

            Assert.That(result, Is.EqualTo("5"));
        }

        /// <summary>
        /// Verifies a named argument for another parameter at the same position is not used for this parameter.
        /// </summary>
        [Test]
        public void ReadCallerArgumentForParameter_WhenPositionHoldsArgumentNamedForOtherParameter_ReturnsEmpty()
        {
            string result = ThirdPartyToolMigrationTimingInvocationRules.ReadCallerArgumentForParameter(
                new[] { "count: 3" },
                new LegacyPlayerLoopTimingParameterDeclaration(0, "int", "value", true));

            Assert.That(result, Is.Empty);
        }

        /// <summary>
        /// Verifies the name of a named argument is read from text before its colon.
        /// </summary>
        [Test]
        public void ReadNamedArgumentName_WhenArgumentIsNamed_ReturnsName()
        {
            string result = ThirdPartyToolMigrationTimingInvocationRules.ReadNamedArgumentName("frames: 2");

            Assert.That(result, Is.EqualTo("frames"));
        }

        /// <summary>
        /// Verifies a conditional expression colon is not mistaken for a named argument.
        /// </summary>
        [Test]
        public void ReadNamedArgumentName_WhenColonBelongsToConditionalExpression_ReturnsEmpty()
        {
            string result = ThirdPartyToolMigrationTimingInvocationRules.ReadNamedArgumentName(
                "ready ? first : second");

            Assert.That(result, Is.Empty);
        }

        private static RemovedLegacyPlayerLoopTimingSignature CreateValueAndTimingSignature(string declaringTypeName)
        {
            return new RemovedLegacyPlayerLoopTimingSignature(
                "Run",
                declaringTypeName,
                new[]
                {
                    new LegacyPlayerLoopTimingParameterDeclaration(0, "int", "value", false),
                    new LegacyPlayerLoopTimingParameterDeclaration(1, "PlayerLoopTiming", "timing", false)
                },
                new[] { new RemovedLegacyPlayerLoopTimingParameter(1, "timing") });
        }

        private static RemovedLegacyPlayerLoopTimingSignature CreateTokenAndTimingSignature()
        {
            return new RemovedLegacyPlayerLoopTimingSignature(
                "Run",
                "Runner",
                new[]
                {
                    new LegacyPlayerLoopTimingParameterDeclaration(0, "CancellationToken", "ct", false),
                    new LegacyPlayerLoopTimingParameterDeclaration(1, "PlayerLoopTiming", "timing", true)
                },
                new[] { new RemovedLegacyPlayerLoopTimingParameter(1, "timing") });
        }
    }
}
