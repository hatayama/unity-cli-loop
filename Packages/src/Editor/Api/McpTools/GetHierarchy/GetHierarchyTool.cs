using System.Threading.Tasks;
using System.Threading;

namespace io.github.hatayama.uLoopMCP
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
    [McpTool(Description = "Get Unity Hierarchy structure for the whole scene, a root path, or GameObject(s) currently selected in the Unity Hierarchy using UseSelection.")]
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
