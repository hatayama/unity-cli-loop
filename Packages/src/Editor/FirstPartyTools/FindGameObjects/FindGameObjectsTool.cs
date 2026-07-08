using System.Threading;
using System.Threading.Tasks;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Bundled tool entry point for GameObject search.
    /// </summary>
    [UnityCliLoopTool]
    public class FindGameObjectsTool : UnityCliLoopTool<FindGameObjectsSchema, FindGameObjectsResponse>
    {
        public override string ToolName => "find-game-objects";

        protected override async Task<FindGameObjectsResponse> ExecuteAsync(FindGameObjectsSchema parameters, CancellationToken ct)
        {
            FindGameObjectsUseCase useCase = new(new GameObjectFinderService(), new ComponentSerializer());
            return await useCase.ExecuteAsync(parameters, ct);
        }
    }
}
