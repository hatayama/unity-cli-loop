using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;

namespace io.github.hatayama.uLoopMCP
{
    public class TestExecutionStateValidationService
    {
        protected virtual bool IsPlaying => EditorApplication.isPlaying;
        protected virtual bool IsCompiling => EditorApplication.isCompiling;
        protected virtual bool IsDomainReloadInProgress => DomainReloadDetectionService.IsDomainReloadInProgress();
        protected virtual bool IsUpdating => EditorApplication.isUpdating;

        public virtual ValidationResult Validate(TestMode testMode)
        {
            if (IsCompiling)
            {
                return ValidationResult.Failure("Tests cannot run while compilation is in progress");
            }

            if (IsDomainReloadInProgress)
            {
                return ValidationResult.Failure("Tests cannot run while domain reload is in progress");
            }

            if (IsUpdating)
            {
                return ValidationResult.Failure("Tests cannot run while the editor is updating");
            }

            if (testMode == TestMode.EditMode && IsPlaying)
            {
                return ValidationResult.Failure("EditMode tests cannot run during play mode");
            }

            return ValidationResult.Success();
        }
    }
}
