using System.Threading;
using System.Threading.Tasks;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Defines the Unity CLI Loop Test Execution operations required by the owning workflow.
    /// </summary>
    public interface IUnityCliLoopTestExecutionService
    {
        Task<UnityCliLoopTestExecutionResult> RunTestsAsync(UnityCliLoopTestExecutionRequest request, CancellationToken ct);
    }

    public enum UnityCliLoopTestMode
    {
        EditMode = 0,
        PlayMode = 1
    }

    public enum TestFilterType
    {
        all = 0,
        exact = 1,
        regex = 2,
        assembly = 3
    }

    /// <summary>
    /// Carries the request data needed for Unity CLI Loop Test Execution behavior.
    /// </summary>
    public sealed class UnityCliLoopTestExecutionRequest
    {
        public UnityCliLoopTestMode TestMode { get; set; } = UnityCliLoopTestMode.EditMode;
        public TestFilterType FilterType { get; set; } = TestFilterType.all;
        public string FilterValue { get; set; } = "";
        public bool SaveBeforeRun { get; set; }
    }

    /// <summary>
    /// Carries the result data produced by Unity CLI Loop Test Execution behavior.
    /// </summary>
    public sealed class UnityCliLoopTestExecutionResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public string CompletedAt { get; set; } = "";
        public int TestCount { get; set; }
        public int PassedCount { get; set; }
        public int FailedCount { get; set; }
        public int SkippedCount { get; set; }
        public string XmlPath { get; set; }
    }
}
