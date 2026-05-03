using System.Threading;
using System.Threading.Tasks;

namespace io.github.hatayama.UnityCliLoop
{
    /// <summary>
    /// GetLogs tool handler - Type-safe implementation using Schema and Response
    /// Retrieves Unity console logs with filtering options
    /// 
    /// Design Reference: @Packages/docs/ARCHITECTURE_Unity.md - UseCase + Tool Pattern (DDD Integration)
    /// 
    /// This Tool class delegates to GetLogsUseCase for business logic execution,
    /// following the UseCase + Tool pattern for separation of concerns.
    /// 
    /// Related classes:
    /// - GetLogsUseCase: Business logic and orchestration
    /// - GetLogsSchema: Type-safe parameter schema
    /// - GetLogsResponse: Type-safe response structure
    /// </summary>
    [McpTool(Description = "Retrieve logs from Unity Console")]
    public class GetLogsTool : AbstractUnityTool<GetLogsSchema, GetLogsResponse>
    {
        public override string ToolName => "get-logs";


        protected override async Task<GetLogsResponse> ExecuteAsync(GetLogsSchema parameters, CancellationToken cancellationToken)
        {
            // Create and execute GetLogsUseCase instance
            GetLogsUseCase useCase = new(new LogRetrievalService(), new LogFilteringService());
            return await useCase.ExecuteAsync(parameters, cancellationToken);
        }
    }
} 