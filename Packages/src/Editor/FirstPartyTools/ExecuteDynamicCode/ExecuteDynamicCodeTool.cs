using System.Threading;
using System.Threading.Tasks;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Dynamic C# code execution tool exposed through the CLI.
    /// Delegates workflow orchestration to the dedicated execute-dynamic-code use case.
    /// </summary>
    [UnityCliLoopTool]
    public class ExecuteDynamicCodeTool : UnityCliLoopTool<ExecuteDynamicCodeSchema, ExecuteDynamicCodeResponse>
    {
        public override string ToolName => "execute-dynamic-code";

        protected override async Task<ExecuteDynamicCodeResponse> ExecuteAsync(
            ExecuteDynamicCodeSchema parameters,
            CancellationToken ct)
        {
            IExecuteDynamicCodeUseCase useCase = DynamicCodeServices.GetExecuteDynamicCodeUseCase();
            return await useCase.ExecuteAsync(parameters, ct);
        }
    }
}
