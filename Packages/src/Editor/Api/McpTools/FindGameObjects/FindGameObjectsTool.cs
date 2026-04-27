using System.Threading;
using System.Threading.Tasks;

namespace io.github.hatayama.uLoopMCP
{
    /// <summary>
    /// Tool to find multiple GameObjects with advanced search criteria
    /// 
    /// Design Reference: @Packages/docs/ARCHITECTURE_Unity.md - UseCase + Tool Pattern (DDD Integration)
    /// 
    /// This Tool class delegates to FindGameObjectsUseCase for business logic execution,
    /// following the UseCase + Tool pattern for separation of concerns.
    /// 
    /// Related classes:
    /// - FindGameObjectsUseCase: Business logic and orchestration
    /// - GameObjectFinderService: Core logic for finding GameObjects
    /// - FindGameObjectsSchema: Search parameters
    /// - FindGameObjectsResponse: Type-safe response structure
    /// </summary>
    [McpTool(Description = "Find GameObjects by search criteria or inspect GameObject(s) currently selected in the Unity Hierarchy using SearchMode.Selected.")]
    public class FindGameObjectsTool : AbstractUnityTool<FindGameObjectsSchema, FindGameObjectsResponse>
    {
        public override string ToolName => "find-game-objects";
        
        protected override async Task<FindGameObjectsResponse> ExecuteAsync(FindGameObjectsSchema parameters, CancellationToken cancellationToken)
        {
            // Create and execute FindGameObjectsUseCase instance
            FindGameObjectsUseCase useCase = new(new GameObjectFinderService(), new ComponentSerializer());
            return await useCase.ExecuteAsync(parameters, cancellationToken);
        }
    }
}
