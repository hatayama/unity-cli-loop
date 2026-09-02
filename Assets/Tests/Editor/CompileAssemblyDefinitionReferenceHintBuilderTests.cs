using System;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Pins the asmdef reference-gap hint appended for CS0246 errors raised from scripts that
    /// belong to an Assembly Definition.
    /// </summary>
    [TestFixture]
    public sealed class CompileAssemblyDefinitionReferenceHintBuilderTests
    {
        private const string ScriptPath = "Assets/Scripts/Gameplay/Player.cs";
        private const string AsmdefPath = "Assets/Scripts/Gameplay/Gameplay.asmdef";

        private const string Cs0246Error =
            "error CS0246: The type or namespace name 'InputSystem' could not be found (are you missing a using directive or an assembly reference?)";

        private const string PrefixlessCs0246Error =
            "CS0246: The type or namespace name 'InputSystem' could not be found (are you missing a using directive or an assembly reference?)";

        private const string Cs0246Hint =
            "error CS0246: 'InputSystem' could not be found from a script under 'Assets/Scripts/Gameplay/Gameplay.asmdef'. If that type lives in another assembly (for example the script was recently moved under a new asmdef), add the declaring assembly to that asmdef's references and run 'uloop compile' again; if the name is a typo, fix the name instead.";

        private const string Cs0234Error =
            "error CS0234: The type or namespace name 'InputSystem' does not exist in the namespace 'UnityEngine' (are you missing an assembly reference?)";

        /// <summary>
        /// What: a CS0246 error from a script under an asmdef produces the reference-gap hint naming that asmdef.
        /// </summary>
        [Test]
        public void Build_WhenCs0246FromAsmdefScript_ReturnsHintNamingAsmdef()
        {
            string[] hints = CompileAssemblyDefinitionReferenceHintBuilder.Build(
                new[] { new CompileErrorOrigin(Cs0246Error, ScriptPath) },
                file => file == ScriptPath ? AsmdefPath : null);

            Assert.That(hints, Is.EqualTo(new[] { Cs0246Hint }));
        }

        /// <summary>
        /// What: a prefix-less CS0246 message produces the same hint.
        /// </summary>
        [Test]
        public void Build_WhenPrefixlessCs0246_ReturnsHint()
        {
            string[] hints = CompileAssemblyDefinitionReferenceHintBuilder.Build(
                new[] { new CompileErrorOrigin(PrefixlessCs0246Error, ScriptPath) },
                file => AsmdefPath);

            Assert.That(hints, Is.EqualTo(new[] { Cs0246Hint }));
        }

        /// <summary>
        /// What: a CS0246 error from a script outside any asmdef stays fail-open with no hint.
        /// </summary>
        [Test]
        public void Build_WhenScriptHasNoAsmdef_ReturnsEmpty()
        {
            string[] hints = CompileAssemblyDefinitionReferenceHintBuilder.Build(
                new[] { new CompileErrorOrigin(Cs0246Error, ScriptPath) },
                file => null);

            Assert.That(hints, Is.EqualTo(Array.Empty<string>()));
        }

        /// <summary>
        /// What: errors other than CS0246 never trigger the hint or the asmdef lookup.
        /// </summary>
        [Test]
        public void Build_WhenNotCs0246_ReturnsEmptyWithoutLookup()
        {
            int lookupCalls = 0;
            string[] hints = CompileAssemblyDefinitionReferenceHintBuilder.Build(
                new[] { new CompileErrorOrigin(Cs0234Error, ScriptPath) },
                file =>
                {
                    lookupCalls++;
                    return AsmdefPath;
                });

            Assert.That(hints, Is.EqualTo(Array.Empty<string>()));
            Assert.That(lookupCalls, Is.EqualTo(0));
        }

        /// <summary>
        /// What: a CS0246 error without a file path cannot be mapped to an asmdef and produces no hint.
        /// </summary>
        [Test]
        public void Build_WhenFileIsMissing_ReturnsEmptyWithoutLookup()
        {
            int lookupCalls = 0;
            string[] hints = CompileAssemblyDefinitionReferenceHintBuilder.Build(
                new[] { new CompileErrorOrigin(Cs0246Error, string.Empty) },
                file =>
                {
                    lookupCalls++;
                    return AsmdefPath;
                });

            Assert.That(hints, Is.EqualTo(Array.Empty<string>()));
            Assert.That(lookupCalls, Is.EqualTo(0));
        }

        /// <summary>
        /// What: several CS0246 errors under the same asmdef produce one hint, for the first missing name.
        /// </summary>
        [Test]
        public void Build_WhenMultipleCs0246UnderSameAsmdef_ReturnsOneHint()
        {
            const string secondError =
                "error CS0246: The type or namespace name 'Keyboard' could not be found (are you missing a using directive or an assembly reference?)";

            string[] hints = CompileAssemblyDefinitionReferenceHintBuilder.Build(
                new[]
                {
                    new CompileErrorOrigin(Cs0246Error, ScriptPath),
                    new CompileErrorOrigin(secondError, "Assets/Scripts/Gameplay/Enemy.cs")
                },
                file => AsmdefPath);

            Assert.That(hints, Is.EqualTo(new[] { Cs0246Hint }));
        }

        /// <summary>
        /// What: a null error list produces no hints.
        /// </summary>
        [Test]
        public void Build_WhenErrorsNull_ReturnsEmpty()
        {
            string[] hints = CompileAssemblyDefinitionReferenceHintBuilder.Build(null, file => AsmdefPath);

            Assert.That(hints, Is.EqualTo(Array.Empty<string>()));
        }
    }
}
