using System;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Verifies which introduced-type diagnostics name a refused declaration, and by which argument.
    /// </summary>
    public sealed class HotReloadIntroducedTypeFailureCodesTests
    {
        private const string FirstArgument = "Game.Units.Spawner";
        private const string SecondArgument = "Game.Units.Pool";

        /// <summary>
        /// What: every reason the planner gives for refusing a declaration by itself names that
        /// declaration first, so a compile error naming the type can be explained by it.
        /// </summary>
        [TestCase("IntroducedTypeGeneric")]
        [TestCase("IntroducedTypePartial")]
        [TestCase("IntroducedTypeRecord")]
        [TestCase("IntroducedTypeNonPublic")]
        [TestCase("IntroducedTypeRefLike")]
        [TestCase("IntroducedTypeUnsafe")]
        [TestCase("IntroducedTypeUnityObject")]
        [TestCase("IntroducedTypeSerializable")]
        [TestCase("IntroducedTypeModuleInitializer")]
        [TestCase("IntroducedTypeUnsupported")]
        [TestCase("IntroducedTypeDelegate")]
        [TestCase("IntroducedTypeNested")]
        [TestCase("IntroducedTypeNestedDeclaration")]
        public void FindRefusedTypeMetadataName_DeclarationReason_ReturnsFirstArgument(string codeName)
        {
            Assert.That(
                HotReloadIntroducedTypeFailureCodes.FindRefusedTypeMetadataName(ParseCode(codeName), CreateArgs()),
                Is.EqualTo(FirstArgument));
        }

        /// <summary>
        /// What: the const reasons name the const first, so the refused type is the second argument.
        /// </summary>
        [TestCase("IntroducedTypeConstValueUnverifiable")]
        [TestCase("IntroducedTypeConstChanged")]
        public void FindRefusedTypeMetadataName_ConstReason_ReturnsSecondArgument(string codeName)
        {
            Assert.That(
                HotReloadIntroducedTypeFailureCodes.FindRefusedTypeMetadataName(ParseCode(codeName), CreateArgs()),
                Is.EqualTo(SecondArgument));
        }

        /// <summary>
        /// What: run-scoped, redefinition, and unresolved-symbol diagnostics refuse no declaration
        /// by name, so no compile error can be attributed to them.
        /// </summary>
        [TestCase("IntroducedTypeSymbolUnresolved")]
        [TestCase("IntroducedTypeArtifactUnusable")]
        [TestCase("IntroducedTypeInputsUnreadable")]
        [TestCase("IntroducedTypeIdentityMismatch")]
        [TestCase("IntroducedTypeChanged")]
        [TestCase("IntroducedTypeMemberBodyChanged")]
        public void FindRefusedTypeMetadataName_OtherReason_ReturnsNull(string codeName)
        {
            Assert.That(
                HotReloadIntroducedTypeFailureCodes.FindRefusedTypeMetadataName(ParseCode(codeName), CreateArgs()),
                Is.Null);
        }

        private static string[] CreateArgs()
        {
            return new[] { FirstArgument, SecondArgument };
        }

        // Why by name: the enum is internal, so a public test method cannot take it as a parameter.
        private static HotReloadWorkerReasonCode ParseCode(string codeName)
        {
            return (HotReloadWorkerReasonCode)Enum.Parse(typeof(HotReloadWorkerReasonCode), codeName);
        }
    }
}
