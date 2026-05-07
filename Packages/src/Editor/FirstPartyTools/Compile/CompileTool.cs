using System.Threading;
using System.Threading.Tasks;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Compile tool handler - Type-safe implementation using Schema and Response
    /// Handles Unity project compilation with optional force recompile
    /// Related classes: CompileUseCase, CompilationStateValidationService, CompilationExecutionService
    /// Design reference: @Packages/docs/ARCHITECTURE_Unity.md - UseCase + Tool Pattern (DDD Integration)
    /// </summary>
    [UnityCliLoopTool]
    public class CompileTool : UnityCliLoopTool<CompileSchema, CompileResponse>
    {
        public override string ToolName => "compile";

        protected override async Task<CompileResponse> ExecuteAsync(CompileSchema parameters, CancellationToken ct)
        {
            CompileUseCase useCase = new();
            UnityCliLoopCompileResult result = await useCase.CompileAsync(ToRequest(parameters), ct);
            return ToResponse(result);
        }

        private static UnityCliLoopCompileRequest ToRequest(CompileSchema parameters)
        {
            if (parameters == null)
            {
                throw new System.ArgumentNullException(nameof(parameters));
            }

            return new UnityCliLoopCompileRequest
            {
                ForceRecompile = parameters.ForceRecompile,
                WaitForDomainReload = parameters.WaitForDomainReload,
                RequestId = parameters.RequestId,
            };
        }

        private static CompileResponse ToResponse(UnityCliLoopCompileResult result)
        {
            if (result == null)
            {
                throw new System.ArgumentNullException(nameof(result));
            }

            CompileResponse response = new(
                success: result.Success,
                errorCount: result.ErrorCount,
                warningCount: result.WarningCount,
                errors: ToCompileIssues(result.Errors),
                warnings: ToCompileIssues(result.Warnings),
                message: result.Message);

            response.ProjectRoot = result.ProjectRoot;
            return response;
        }

        private static CompileIssue[] ToCompileIssues(UnityCliLoopCompileIssue[] issues)
        {
            if (issues == null)
            {
                return null;
            }

            CompileIssue[] mappedIssues = new CompileIssue[issues.Length];
            for (int i = 0; i < issues.Length; i++)
            {
                UnityCliLoopCompileIssue issue = issues[i];
                mappedIssues[i] = new CompileIssue(issue.Message, issue.File, issue.Line);
            }

            return mappedIssues;
        }
    }
}
