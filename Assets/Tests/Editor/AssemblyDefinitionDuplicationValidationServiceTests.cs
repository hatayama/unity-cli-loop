using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies the duplicate Assembly Definition name check against this project's own asmdefs, read-only.
    /// </summary>
    public sealed class AssemblyDefinitionDuplicationValidationServiceTests
    {
        [Test]
        public void ValidateNoDuplicateAsmdefNames_WithThisProjectsUniqueNames_Succeeds()
        {
            // Verifies the check reads each asmdef's declared name and accepts a project whose names are unique.
            AssemblyDefinitionDuplicationValidationService service = new AssemblyDefinitionDuplicationValidationService();

            ValidationResult result = service.ValidateNoDuplicateAsmdefNames();

            Assert.That(result.IsValid, Is.True, result.ErrorMessage);
        }
    }
}
