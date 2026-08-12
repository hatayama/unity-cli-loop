using System;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.DynamicCodeToolTests
{
    /// <summary>
    /// Verifies the immutable compiler-settings snapshot used by dynamic compilation.
    /// </summary>
    [TestFixture]
    public sealed class RoslynCompilerOptionsTests
    {
        /// <summary>
        /// Verifies empty and whitespace-only define symbols are excluded while valid symbols are copied.
        /// </summary>
        [Test]
        public void Constructor_FiltersWhitespaceAndCopiesDefineSymbols()
        {
            string[] defineSymbols =
            {
                "UNITY_EDITOR",
                null,
                "",
                "  ",
                "CUSTOM_DEFINE"
            };

            RoslynCompilerOptions options = new(defineSymbols, true, emitDebugCode: false);
            defineSymbols[0] = "MUTATED_AFTER_CAPTURE";

            Assert.That(
                options.DefineSymbols,
                Is.EqualTo(new[] { "UNITY_EDITOR", "CUSTOM_DEFINE" }));
            Assert.That(options.AllowUnsafeCode, Is.True);
            Assert.That(options.EmitDebugCode, Is.False);
        }

        /// <summary>
        /// Verifies an empty define-symbol collection produces an empty immutable snapshot.
        /// </summary>
        [Test]
        public void Constructor_WithEmptyDefineSymbols_CapturesEmptySnapshot()
        {
            RoslynCompilerOptions options = new(Array.Empty<string>(), false, emitDebugCode: true);

            Assert.That(options.DefineSymbols, Is.Empty);
            Assert.That(options.AllowUnsafeCode, Is.False);
            Assert.That(options.EmitDebugCode, Is.True);
        }

    }
}
