using System.Threading.Tasks;
using System.Threading;

namespace io.github.hatayama.UnityCliLoop
{
    /// <summary>
    /// Tool to retrieve Unity Hierarchy information in AI-friendly format
    /// 
    /// Design Reference: @Packages/docs/ARCHITECTURE_Unity.md - UseCase + Tool Pattern (DDD Integration)
    /// 
    /// This Tool class delegates to GetHierarchyUseCase for business logic execution,
    /// following the UseCase + Tool pattern for separation of concerns.
    /// 
    /// Related classes:
    /// - GetHierarchyUseCase: Business logic and orchestration
    /// - HierarchyService: Core logic for hierarchy traversal
    /// - HierarchySerializer: JSON formatting logic
    /// - HierarchyNode: Data structure for hierarchy nodes
    /// - HierarchyNodeNested: Nested hierarchy structure
    /// - HierarchyResultExporter: File export functionality
    /// - GetHierarchySchema: Type-safe parameter schema
    /// - GetHierarchyResponse: Type-safe response structure
    /// </summary>
    [McpTool(Description = "Get Unity Hierarchy structure for the whole scene, a root path, or selected GameObject descendants using UseSelection. Use this when the child tree, parent-child structure, or descendants under the selection are needed. Use find-game-objects for selected object details and component properties.")]
    public class GetHierarchyTool : AbstractUnityTool<GetHierarchySchema, GetHierarchyResponse>
    {
        public override string ToolName => "get-hierarchy";
        
        protected override async Task<GetHierarchyResponse> ExecuteAsync(GetHierarchySchema parameters, CancellationToken cancellationToken)
        {
            // Create and execute GetHierarchyUseCase instance
            GetHierarchyUseCase useCase = new(new HierarchyService(), new HierarchySerializer());
            return await useCase.ExecuteAsync(parameters, cancellationToken);
        }
    }
}
