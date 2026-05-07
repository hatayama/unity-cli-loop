using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Responsible for temporal cohesion of Unity Hierarchy retrieval processing
    /// Processing sequence: 1. Hierarchy information retrieval, 2. Data conversion, 3. Response size determination and file output
    /// Related classes: GetHierarchyTool, HierarchyService, HierarchySerializer, HierarchyResultExporter
    /// Design reference: @Packages/docs/ARCHITECTURE_Unity.md - UseCase + Tool Pattern (DDD Integration)
    /// </summary>
    public class GetHierarchyUseCase : IUnityCliLoopHierarchyService
    {
        private readonly HierarchyService _hierarchyService;
        private readonly HierarchySerializer _hierarchySerializer;

        public GetHierarchyUseCase(HierarchyService hierarchyService, HierarchySerializer hierarchySerializer)
        {
            _hierarchyService = hierarchyService ?? throw new System.ArgumentNullException(nameof(hierarchyService));
            _hierarchySerializer = hierarchySerializer ?? throw new System.ArgumentNullException(nameof(hierarchySerializer));
        }
        /// <summary>
        /// Execute Unity Hierarchy retrieval processing
        /// </summary>
        /// <param name="parameters">Hierarchy retrieval parameters</param>
        /// <param name="ct">Cancellation control token</param>
        /// <returns>Hierarchy retrieval result</returns>
        public Task<UnityCliLoopHierarchyResult> ExecuteAsync(UnityCliLoopHierarchyRequest parameters, CancellationToken ct)
        {
            if (parameters == null)
            {
                throw new System.ArgumentNullException(nameof(parameters));
            }

            try
            {
                // 1. Hierarchy information retrieval
                HierarchyOptions options = new()                {
                    IncludeInactive = parameters.IncludeInactive,
                    MaxDepth = parameters.MaxDepth,
                    RootPath = parameters.RootPath,
                    IncludeComponents = parameters.IncludeComponents,
                    UseSelection = parameters.UseSelection
                };
                
                ct.ThrowIfCancellationRequested();
                
                List<HierarchyNode> nodes = _hierarchyService.GetHierarchyNodes(options);
                HierarchyContext context = _hierarchyService.GetCurrentContext() ?? new HierarchyContext("editor", string.Empty, 0, 0);

                // 2. Data conversion to scene-grouped structure
                ct.ThrowIfCancellationRequested();
                HierarchySerializationOptions serOptions = new()                {
                    IncludePaths = parameters.IncludePaths,
                    UseComponentsLut = parameters.UseComponentsLut
                };
                HierarchySerializationResult result = _hierarchySerializer.BuildGroups(nodes, context, serOptions);

                // 3. Always export to JSON
                ct.ThrowIfCancellationRequested();
                string filePath = HierarchyResultExporter.ExportHierarchyResults(result.Groups, result.Context);
                string message = "Hierarchy data saved below. Open the JSON to read 'Context' and 'Hierarchy'.";
                return Task.FromResult(new UnityCliLoopHierarchyResult(filePath, message));
            }
            catch (System.OperationCanceledException)
            {
                throw; // Re-throw cancellation exceptions
            }
            catch (System.Exception ex)
            {
                // Log the error and re-throw
                VibeLogger.LogError("get_hierarchy_failed", $"Failed to get hierarchy: {ex.Message}", ex);
                throw new System.InvalidOperationException($"Failed to retrieve hierarchy: {ex.Message}", ex);
            }
        }

        public Task<UnityCliLoopHierarchyResult> GetHierarchyAsync(UnityCliLoopHierarchyRequest request, CancellationToken ct)
        {
            return ExecuteAsync(request, ct);
        }
    }
}
