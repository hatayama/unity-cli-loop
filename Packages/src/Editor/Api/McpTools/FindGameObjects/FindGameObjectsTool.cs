using System.Threading;
using System.Threading.Tasks;

namespace io.github.hatayama.UnityCliLoop
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
    [McpTool(Description = "Use first when the user asks about the currently selected GameObject in the Unity Hierarchy. Inspect selected object details and component properties with SearchMode.Selected before using execute-dynamic-code. Also search by name, path, regex, tag, layer, or required components. Use get-hierarchy when the child tree under the selection is needed.")]
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
