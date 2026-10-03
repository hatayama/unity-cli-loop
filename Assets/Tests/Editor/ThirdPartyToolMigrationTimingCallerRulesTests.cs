using System;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies how timing migration rewrites callers of methods whose PlayerLoopTiming parameter was removed.
    /// </summary>
    public sealed class ThirdPartyToolMigrationTimingCallerRulesTests
    {
        /// <summary>
        /// Verifies a method group reference is left untouched while a real call is rewritten.
        /// </summary>
        [Test]
        public void RemoveLegacyPlayerLoopTimingCallerArgumentsInCode_WhenMethodIsReferencedAsMethodGroup_RewritesOnlyCall()
        {
            string source =
                "class Runner\n{\n    void Call()\n    {\n        Action<int, PlayerLoopTiming> action = Run;\n" +
                "        Run(1, PlayerLoopTiming.Update);\n    }\n}\n";

            (string content, int replacementCount) =
                ThirdPartyToolMigrationTimingCallerRules.RemoveLegacyPlayerLoopTimingCallerArgumentsInCode(
                    source,
                    new[] { CreateValueAndTimingSignature() },
                    Array.Empty<string>());

            Assert.That(
                content,
                Is.EqualTo(
                    "class Runner\n{\n    void Call()\n    {\n        Action<int, PlayerLoopTiming> action = Run;\n" +
                    "        Run(1);\n    }\n}\n"));
            Assert.That(replacementCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies a call whose argument list never closes is left untouched.
        /// </summary>
        [Test]
        public void RemoveLegacyPlayerLoopTimingCallerArgumentsInCode_WhenArgumentListIsUnterminated_KeepsSource()
        {
            string source = "class Runner\n{\n    void Call() { Run(1, PlayerLoopTiming.Update";

            (string content, int replacementCount) =
                ThirdPartyToolMigrationTimingCallerRules.RemoveLegacyPlayerLoopTimingCallerArgumentsInCode(
                    source,
                    new[] { CreateValueAndTimingSignature() },
                    Array.Empty<string>());

            Assert.That(content, Is.EqualTo(source));
            Assert.That(replacementCount, Is.EqualTo(0));
        }

        /// <summary>
        /// Verifies a call nested in the arguments of another call to the same method is rewritten as well as the outer call.
        /// </summary>
        [Test]
        public void RemoveLegacyPlayerLoopTimingCallerArgumentsInCode_WhenCallIsNestedInSameMethodCall_RewritesBothCalls()
        {
            string source =
                "class Runner\n{\n    void Call()\n    {\n" +
                "        Run(Run(1, PlayerLoopTiming.Update), PlayerLoopTiming.Update);\n    }\n}\n";

            (string content, int replacementCount) =
                ThirdPartyToolMigrationTimingCallerRules.RemoveLegacyPlayerLoopTimingCallerArgumentsInCode(
                    source,
                    new[] { CreateValueAndTimingSignature() },
                    Array.Empty<string>());

            Assert.That(
                content,
                Is.EqualTo("class Runner\n{\n    void Call()\n    {\n        Run(Run(1));\n    }\n}\n"));
            Assert.That(replacementCount, Is.EqualTo(2));
        }

        private static RemovedLegacyPlayerLoopTimingSignature CreateValueAndTimingSignature()
        {
            return new RemovedLegacyPlayerLoopTimingSignature(
                "Run",
                "Runner",
                new[]
                {
                    new LegacyPlayerLoopTimingParameterDeclaration(0, "int", "value", false),
                    new LegacyPlayerLoopTimingParameterDeclaration(1, "PlayerLoopTiming", "timing", false)
                },
                new[] { new RemovedLegacyPlayerLoopTimingParameter(1, "timing") });
        }
    }
}
