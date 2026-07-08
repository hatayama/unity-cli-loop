using System.Threading.Tasks;
using System.Threading;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Bundled tool entry point for hierarchy export.
    /// </summary>
    [UnityCliLoopTool]
    public class GetHierarchyTool : UnityCliLoopTool<GetHierarchySchema, GetHierarchyResponse>
    {
        public override string ToolName => "get-hierarchy";

        protected override async Task<GetHierarchyResponse> ExecuteAsync(GetHierarchySchema parameters, CancellationToken ct)
        {
            GetHierarchyUseCase useCase = new(new HierarchyService(), new HierarchySerializer());
            return await useCase.ExecuteAsync(parameters, ct);
        }
    }
}
