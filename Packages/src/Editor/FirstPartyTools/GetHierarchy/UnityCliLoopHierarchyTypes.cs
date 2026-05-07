using System.Threading;
using System.Threading.Tasks;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Defines the Unity CLI Loop Hierarchy operations required by the owning workflow.
    /// </summary>
    public interface IUnityCliLoopHierarchyService
    {
        Task<UnityCliLoopHierarchyResult> GetHierarchyAsync(UnityCliLoopHierarchyRequest request, CancellationToken ct);
    }

    /// <summary>
    /// Carries the request data needed for Unity CLI Loop Hierarchy behavior.
    /// </summary>
    public sealed class UnityCliLoopHierarchyRequest
    {
        public bool IncludeInactive { get; set; } = true;
        public int MaxDepth { get; set; } = -1;
        public string RootPath { get; set; }
        public bool IncludeComponents { get; set; } = true;
        public bool IncludePaths { get; set; }
        public string UseComponentsLut { get; set; } = "auto";
        public bool UseSelection { get; set; }
    }

    /// <summary>
    /// Carries the result data produced by Unity CLI Loop Hierarchy behavior.
    /// </summary>
    public sealed class UnityCliLoopHierarchyResult
    {
        public string FilePath { get; }
        public string Message { get; }

        public UnityCliLoopHierarchyResult(string filePath, string message)
        {
            FilePath = filePath ?? string.Empty;
            Message = message ?? string.Empty;
        }
    }
}
