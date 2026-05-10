using System.Diagnostics;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Domain
{
    /// <summary>
    /// Decides whether tool execution and discovery should bypass disabled settings for dependency-error reporting.
    /// </summary>
    public static class ToolExecutionAvailability
    {
        public static bool ShouldReportDependencyUnavailableBeforeDisabled(string toolName)
        {
            return ShouldReportDependencyUnavailableBeforeDisabled(
                toolName,
                IsTestFrameworkAvailable);
        }

        public static bool ShouldReportDependencyUnavailableBeforeDisabled(
            string toolName,
            bool isTestFrameworkAvailable)
        {
            Debug.Assert(!string.IsNullOrEmpty(toolName), "toolName must not be null or empty");

            return toolName == UnityCliLoopConstants.TOOL_NAME_RUN_TESTS && !isTestFrameworkAvailable;
        }

        public static bool ShouldExposeInRegisteredTools(string toolName, bool isToolEnabled)
        {
            return ShouldExposeInRegisteredTools(
                toolName,
                isToolEnabled,
                IsTestFrameworkAvailable);
        }

        public static bool ShouldExposeInRegisteredTools(
            string toolName,
            bool isToolEnabled,
            bool isTestFrameworkAvailable)
        {
            Debug.Assert(!string.IsNullOrEmpty(toolName), "toolName must not be null or empty");

            return isToolEnabled
                || ShouldReportDependencyUnavailableBeforeDisabled(toolName, isTestFrameworkAvailable);
        }

        public static bool IsTestFrameworkAvailable
        {
            get
            {
#if ULOOP_HAS_TEST_FRAMEWORK
                return true;
#else
                return false;
#endif
            }
        }
    }
}
