using System.Threading;
using System.Threading.Tasks;

namespace io.github.hatayama.uLoopMCP
{
    /// <summary>
    /// Test execution tool handler - Type-safe implementation using Schema and Response
    /// Executes tests using Unity Test Runner and returns the results
    /// 
    /// Design Reference: @Packages/docs/ARCHITECTURE_Unity.md - UseCase + Tool Pattern (DDD Integration)
    /// 
    /// This Tool class delegates to RunTestsUseCase for business logic execution,
    /// following the UseCase + Tool pattern for separation of concerns.
    /// 
    /// Related classes:
    /// - RunTestsUseCase: Business logic and orchestration
    /// - RunTestsSchema: Type-safe parameter schema
    /// - RunTestsResponse: Type-safe response structure
    /// </summary>
    [McpTool(
        Description = "Execute Unity Test Runner with advanced filtering options - exact test methods, regex patterns for classes/namespaces, assembly filtering"
    )]
    public class RunTestsTool : AbstractUnityTool<RunTestsSchema, RunTestsResponse>
    {
        public override string ToolName => McpConstants.TOOL_NAME_RUN_TESTS;

        protected override async Task<RunTestsResponse> ExecuteAsync(RunTestsSchema parameters, CancellationToken ct)
        {
            // Create and execute RunTestsUseCase instance
            RunTestsUseCase useCase = new();
            return await useCase.ExecuteAsync(parameters, ct);
        }

    }
}
