using System.Threading.Tasks;
using System.Threading;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Application
{
    /// <summary>
    /// UseCase base class - Responsible for temporal cohesion
    /// New instances are created each time and disposed after Execute completion
    /// Related classes: UnityCliLoopTool, UnityCliLoopToolSchema, UnityCliLoopToolResponse
    /// Design reference: @Packages/docs/ARCHITECTURE_Unity.md - UseCase Layer (Domain Workflow Orchestration)
    /// </summary>
    /// <typeparam name="TSchema">Schema type for tool parameters</typeparam>
    /// <typeparam name="TResponse">Response type for tool results</typeparam>
    public abstract class AbstractUseCase<TSchema, TResponse>
        where TSchema : UnityCliLoopToolSchema
        where TResponse : UnityCliLoopToolResponse
    {
        /// <summary>
        /// UseCase execution method - The only public method
        /// Responsible for temporal cohesion (executing multiple operations in sequence)
        /// </summary>
        /// <param name="parameters">Type-safe parameters</param>
        /// <param name="cancellationToken">Cancellation control token</param>
        /// <returns>Type-safe execution result</returns>
        public abstract Task<TResponse> ExecuteAsync(TSchema parameters, CancellationToken cancellationToken);
    }
}