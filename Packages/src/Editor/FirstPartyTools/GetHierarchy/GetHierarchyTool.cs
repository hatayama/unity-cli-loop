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
            UnityCliLoopHierarchyResult result = await useCase.GetHierarchyAsync(ToRequest(parameters), ct);
            return new GetHierarchyResponse(result.FilePath, result.Message);
        }

        private static UnityCliLoopHierarchyRequest ToRequest(GetHierarchySchema parameters)
        {
            if (parameters == null)
            {
                throw new System.ArgumentNullException(nameof(parameters));
            }

            return new UnityCliLoopHierarchyRequest
            {
                IncludeInactive = parameters.IncludeInactive,
                MaxDepth = parameters.MaxDepth,
                RootPath = parameters.RootPath,
                IncludeComponents = parameters.IncludeComponents,
                IncludePaths = parameters.IncludePaths,
                UseComponentsLut = parameters.UseComponentsLut,
                UseSelection = parameters.UseSelection,
            };
        }
    }
}
