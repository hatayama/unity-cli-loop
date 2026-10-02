using System;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies how timing migration removes legacy PlayerLoopTiming parameters from method declarations.
    /// </summary>
    public sealed class ThirdPartyToolMigrationTimingDeclarationRulesTests
    {
        /// <summary>
        /// Verifies a method declaration whose parameter list never closes is left untouched.
        /// </summary>
        [Test]
        public void RemoveLegacyPlayerLoopTimingParametersInCode_WhenParameterListIsUnterminated_KeepsSource()
        {
            string source = "class Runner\n{\n    void Run(PlayerLoopTiming timing";

            (string content, int replacementCount, RemovedLegacyPlayerLoopTimingSignature[] removedSignatures) =
                ThirdPartyToolMigrationTimingDeclarationRules.RemoveLegacyPlayerLoopTimingParametersInCode(
                    source,
                    Array.Empty<string>(),
                    true,
                    new[] { "Helper" });

            Assert.That(content, Is.EqualTo(source));
            Assert.That(replacementCount, Is.EqualTo(0));
            Assert.That(removedSignatures, Is.Empty);
        }
    }
}
