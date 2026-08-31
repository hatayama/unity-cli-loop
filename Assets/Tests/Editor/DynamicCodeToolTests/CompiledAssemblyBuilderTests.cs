using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor.Compilation;
using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.DynamicCodeToolTests
{
    /// <summary>
    /// Test fixture that verifies Compiled Assembly Builder behavior.
    /// </summary>
    [TestFixture]
    public class CompiledAssemblyBuilderTests
    {
        [Test]
        public void CreateUniqueCompilationName_WhenClassNameContainsPathCharacters_ShouldSanitizeFileName()
        {
            string compilationName = CompiledAssemblyBuilder.CreateUniqueCompilationName(
                "Bad/Name:With\\Separators",
                42);

            Assert.That(compilationName, Does.EndWith("_42"));
            Assert.That(compilationName, Does.Not.Contain("/"));
            Assert.That(compilationName, Does.Not.Contain("\\"));
            Assert.That(compilationName, Does.Not.Contain(":"));
        }

        // What: CS1503 (argument type mismatch), which hoisting can cause by turning a
        // narrowing literal argument into an int variable, must trigger the non-hoisted
        // recompile fallback like the other hoisting-caused error codes.
        [Test]
        public void ShouldRetryWithoutLiteralHoisting_WhenDiagnosticsContainCS1503_ReturnsTrue()
        {
            PreparedDynamicCode preparedCode = new(
                "// prepared source",
                isScriptMode: true,
                new List<HoistedLiteralBinding> { new("arg0", "int", 255) });
            CompilerDiagnostics diagnostics = CompilerDiagnostics.FromMessages(new[]
            {
                new CompilerMessage
                {
                    message = "error CS1503: Argument 1: cannot convert from 'int' to 'byte'",
                    type = CompilerMessageType.Error
                }
            });

            bool shouldRetry = CompiledAssemblyBuilder.ShouldRetryWithoutLiteralHoisting(preparedCode, diagnostics);

            Assert.That(shouldRetry, Is.True);
        }

        // What: without any hoisted literal bindings, a CS1503 error cannot be caused by
        // hoisting, so the fallback must not be triggered.
        [Test]
        public void ShouldRetryWithoutLiteralHoisting_WhenNoLiteralsWereHoisted_ReturnsFalse()
        {
            PreparedDynamicCode preparedCode = new(
                "// prepared source",
                isScriptMode: true,
                new List<HoistedLiteralBinding>());
            CompilerDiagnostics diagnostics = CompilerDiagnostics.FromMessages(new[]
            {
                new CompilerMessage
                {
                    message = "error CS1503: Argument 1: cannot convert from 'int' to 'byte'",
                    type = CompilerMessageType.Error
                }
            });

            bool shouldRetry = CompiledAssemblyBuilder.ShouldRetryWithoutLiteralHoisting(preparedCode, diagnostics);

            Assert.That(shouldRetry, Is.False);
        }
    }
}
