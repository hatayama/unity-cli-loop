using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;

namespace io.github.hatayama.uLoopMCP
{
    public class TestExecutionStateValidationService
    {
        protected virtual bool IsPlaying => EditorApplication.isPlaying;

        public virtual ValidationResult Validate(TestMode testMode)
        {
            if (testMode == TestMode.EditMode && IsPlaying)
            {
                return ValidationResult.Failure("EditMode tests cannot run during play mode");
            }

            return ValidationResult.Success();
        }
    }
}
